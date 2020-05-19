using System;
using Terraria;
using TShockAPI;
using System.IO;
using TerrariaApi.Server;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;
using TShockAPI.DB;
using System.Text;
using System.Linq;

namespace BlocksRegenerator
{
    [ApiVersion(2, 1)]
    public class BR : TerrariaPlugin
    {
        public override string Author => new string[] { "Michael Diagenov", "Lord Diogen", "Darth Diogen" }[new Random().Next(0, 3)];
        public override string Description => "Предназначен для регенерации определенных блоков в регионе BR через определенный промежуток времени.";
        public override string Name => "Blocks Regenerator (BR)";
        public override Version Version => base.Version;
        public BR(Main game) : base(game) { }
        static BRConfig config = new BRConfig()
        {
            tiles = new List<ushort>(10),
            time = 60,
            status = false,
            ping = true
        };
        static List<MyTile> MyTiles = new List<MyTile>();
        static Dictionary<string, ushort> Tiles = new Dictionary<string, ushort>();
        static string path = TShock.SavePath + "\\BRConfig.json";
        static Timer timer;
        public override void Initialize()
        {
            BRJson(false);
            if (config.status)
            {
                timer = new Timer(BRTimer, null, 60000 * config.time, 60000 * config.time);
                ServerApi.Hooks.NetGetData.Register(this, OnGetData);
            }
            StringBuilder sb = new StringBuilder();
            foreach (System.Reflection.FieldInfo fi in typeof(Terraria.ID.TileID).GetFields())
            {
                for (int i = 0; i < fi.Name.Length; i++)
                {
                    if (char.IsUpper(fi.Name[i]))
                        sb.Append(" ").Append(char.ToLower(fi.Name[i]));
                    else
                        sb.Append(fi.Name[i]);
                }
                Tiles.Add(sb.ToString(1, sb.Length - 1), (ushort)fi.GetValue(null));
                sb.Clear();
            }
            Commands.ChatCommands.Add(new Command("br.command", BRCommand, "br"));
        }
        void BRCommand(CommandArgs e)
        {
            if (e.Parameters.Count == 0 || (e.Parameters[0] == "help" && (e.Parameters.Count == 1 || e.Parameters[1] == "1")))
            {
                e.Player.SendMessage("Синтаксис ([c/74667a:1]/[c/74667a:2]):", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:1]) [c/74667a:/br add] \"[c/74667a:tile id]/[c/74667a:name]\" — добавляет блок в список регенерируемых блоков.", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:2]) [c/74667a:/br del] \"[c/74667a:tile id]/[c/74667a:name]\" — удаляет блок из списка регенерируемых блоков.", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:3]) [c/74667a:/br list] — выдает список регенерируемых блоков.", new Color(171, 158, 38));
                e.Player.SendMessage("Следующая страница — [c/74667a:/br help 2].", new Color(171, 158, 38));
            }
            else if (e.Parameters[0] == "help" && int.TryParse(e.Parameters[1], out int num) && num > 1)
            {
                e.Player.SendMessage("Синтаксис ([c/74667a:2]/[c/74667a:2]):", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:4]) [c/74667a:/br status] — включает/выключает режим регенерации блоков.", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:5]) [c/74667a:/br ping] — включает/выключает пинг при регенерации блоков.", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:6]) [c/74667a:/br time] \"[c/74667a:minutes]\" — устанавливает период времени регенерации блоков.", new Color(171, 158, 38));
                e.Player.SendMessage("[c/74667a:7]) [c/74667a:/br info] — выдает некоторую информацию о параметрах регенерации блоков.", new Color(171, 158, 38));
            }
            else if (e.Parameters.Count == 2 && (e.Parameters[0] == "add" || e.Parameters[0] == "del"))
            {
                if (DeleteOrAddTile(e.Parameters[0] == "del", e.Player, e.Parameters[1]))
                    BRJson(true);
            }
            else if (e.Parameters[0] == "list")
            {
                int maxPage = 0, page = 0, count = config.tiles.Count;
                for (; count > 0; count -= 15)
                    maxPage++;
                if (e.Parameters.Count > 1 && int.TryParse(e.Parameters[1], out page) && page > maxPage)
                    page = maxPage;
                var list = config.tiles.Skip(page * 15 - 15).Take(page == maxPage ? 15 + count : 15);
                var list2 = Tiles.OfType<KeyValuePair<string, ushort>>().Where(kvp => list.Any(t => t == kvp.Value)).Select(kvp => new SearchTile(kvp.Key, kvp.Value).ToString());
                e.Player.SendMessage($"Список регенерируемых блоков ([c/74667a:{page}]/[c/74667a:{maxPage}): {string.Join(", ", list2)}.", new Color(171, 158, 38));
            }
            else if (e.Parameters[0] == "status")
            {
                config.status = !config.status;
                if (config.status)
                {
                    timer = new Timer(BRTimer, null, 60000 * config.time, 60000 * config.time);
                    ServerApi.Hooks.NetGetData.Register(this, OnGetData);
                }
                else
                {
                    timer.Dispose();
                    ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                }
                e.Player.SendMessage($"Состояние status'a регенерации блоков: [c/74667a:{config.status}].", new Color(67, 143, 50));
                BRJson(true);
            }
            else if (e.Parameters[0] == "ping")
            {
                config.ping = !config.ping;
                e.Player.SendMessage($"Состояние ping'a при регенерации блоков: [c/74667a:{config.ping}].", new Color(67, 143, 50));
                BRJson(true);
            }
            else if (e.Parameters.Count == 2 && (e.Parameters[0] == "time"))
            {
                if(!short.TryParse(e.Parameters[1], out short minutes))
                    e.Player.SendMessage("Некорректный синтаксис!", new Color(168, 49, 49));
                else if(minutes > 1440)
                    e.Player.SendMessage("Период времени регенерации блоков не может превышать [c/74667a:24] часа!", new Color(168, 49, 49));
                else
                {
                    config.time = minutes;
                    if (config.status)
                    {
                        timer.Change(60000 * config.time, 60000 * config.time);
                    }
                    e.Player.SendMessage($"Установлен новый период времени регенерации блоков: [c/74667a:{minutes}]!", new Color(67, 143, 50));
                    BRJson(true);
                }
            }
            else if (e.Parameters[0] == "info")
            {
                e.Player.SendMessage("Параметры регенерации блоков:", new Color(171, 158, 38));
                e.Player.SendMessage($"[c/74667a:1]) Состояние status'a: [c/74667a:{config.status}].", new Color(171, 158, 38));
                e.Player.SendMessage($"[c/74667a:2]) Состояние ping'a: [c/74667a:{config.ping}].", new Color(171, 158, 38));
                e.Player.SendMessage($"[c/74667a:3]) Период времени регенерации блоков в минутах: [c/74667a:{config.time}].", new Color(171, 158, 38));
                e.Player.SendMessage($"[c/74667a:4]) Количество регенерируемых блоков: [c/74667a:{config.tiles.Count()}].", new Color(171, 158, 38));
            }
            else e.Player.SendMessage("Некорректный синтаксис!", new Color(168, 49, 49));
        }
        void OnGetData(GetDataEventArgs e)
        {
            if(e.MsgID == PacketTypes.Tile && e.Length >= 4)
            {
                using (var reader = new BinaryReader(new MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte Action = reader.ReadByte();
                    short X = reader.ReadInt16();
                    short Y = reader.ReadInt16();
                    if (X > 0 && Y > 0 && X < Main.maxTilesX && Y < Main.maxTilesY && TShock.Regions.InAreaRegionName(X, Y).Any(s => s == "BR"))
                    {
                        if (Main.tile[X, Y] != null && Action == 0 && config.tiles.Any(t => t == Main.tile[X, Y].type))
                            MyTiles.Add(new MyTile(X, Y, Main.tile[X, Y].type));
                        else
                        {
                            e.Handled = true;
                            TShock.Players[e.Msg.whoAmI].SendTileSquare(X, Y, 3);
                        }
                    }
                }
            }
        }
        void BRJson(bool writeOrRead)
        {
            if (writeOrRead)
                File.WriteAllText(path, JsonConvert.SerializeObject(config));
            else if(File.Exists(path))
                config = JsonConvert.DeserializeObject<BRConfig>(File.ReadAllText(path));
        }
        void BRTimer(object obj)
        {
            Region region = TShock.Regions.GetRegionByName("BR");
            if (MyTiles.Count > 0 && region != null)
            {
                for (byte i = 0; i < MyTiles.Count; i++)
                    Main.tile[MyTiles[i].X, MyTiles[i].Y].ResetToType(MyTiles[i].Type);
                MyTiles.Clear();
                TSPlayer.All.SendData(PacketTypes.TileSendSection, null, region.Area.X, region.Area.Y, region.Area.Width, region.Area.Height);
                TSPlayer.All.SendData(PacketTypes.TileFrameSection, null, Netplay.GetSectionX(region.Area.X), Netplay.GetSectionY(region.Area.Y), Netplay.GetSectionX(region.Area.X + region.Area.Width), Netplay.GetSectionY(region.Area.Y + region.Area.Height));
                if (config.ping)
                {
                    Color color = new Color(173, 137, 69);
                    foreach (TSPlayer plr in TShock.Players.Where(p => p != null && p.ConnectionAlive && p.RealPlayer && p.HasPermission("br.ping")))
                    {
                        NetMessage.SendData(58, plr.Index, -1, null, plr.Index, -0.2410251f);
                        plr.SendData(PacketTypes.CreateCombatTextExtended, "Blocks Regenerated!", (int)color.PackedValue, plr.X, plr.Y);
                        Thread.Sleep(500);
                        NetMessage.SendData(58, plr.Index, -1, null, plr.Index, -0.6057936f);
                    }
                }
            }
        }
        SearchTile[] SearchTile(string StringTile)
        {
            if (int.TryParse(StringTile, out int TileID) && TileID >= 0 && TileID < Main.maxTileSets)
                return new SearchTile[1] { new SearchTile(null, (ushort)TileID) };
            List<SearchTile> list = new List<SearchTile>();
            foreach (KeyValuePair<string, ushort> tile in Tiles)
            {
                if (tile.Key == StringTile)
                    return new SearchTile[1] { new SearchTile(null, tile.Value) };
                if (tile.Key.StartsWith(StringTile))
                {
                    list.Add(new SearchTile(tile.Key, tile.Value));
                }
            }
            return list.ToArray();
        }
        bool DeleteOrAddTile(bool deleteOrAdd, TSPlayer plr, string parameter)
        {
            SearchTile[] tiles = SearchTile(parameter.ToLowerInvariant());
            if (tiles.Length == 0)
                plr.SendMessage($"Некорректный tile: [c/74667a:{parameter}]!", new Color(168, 49, 49));
            else if (tiles.Length > 2)
                plr.SendMessage($"Найдено больше одного tile: {string.Join(", ", tiles.Select(st => st.ToString()))}!", new Color(168, 49, 49));
            else
            {
                if (deleteOrAdd ? config.tiles.All(t => t != tiles[0].Type) : config.tiles.Any(t => t == tiles[0].Type))
                {
                    plr.SendMessage(deleteOrAdd ? $"Tile [c/74667a:{tiles[0].Type}] нет в списке регенерируемых блоков!" : $"Tile [c/74667a:{tiles[0].Type}] уже есть в списке регенерируемых блоков!", new Color(168, 49, 49));
                }
                else if (deleteOrAdd)
                {
                    config.tiles.Remove(tiles[0].Type);
                    plr.SendMessage($"Из списка регенерируемых блоков удален tile [c/74667a:{tiles[0].Type}]!", new Color(67, 143, 50));
                    return true;
                }
                else
                {
                    config.tiles.Add(tiles[0].Type);
                    plr.SendMessage($"В список регенерируемых блоков добавлен tile [c/74667a:{tiles[0].Type}]!", new Color(67, 143, 50));
                    return true;
                }
            }
            return false;
        } 
        protected override void Dispose(bool disposing)
        {
            if (disposing && config.status)
            {
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    struct MyTile
    {
        internal MyTile(short X, short Y, ushort Type)
        {
            this.X = X;
            this.Y = Y;
            this.Type = Type;
        }
        internal ushort Type;
        internal short X;
        internal short Y;
    }
    struct SearchTile
    {
        internal SearchTile(string name, ushort type)
        {
            Name = name;
            Type = type;
        }
        internal string Name;
        internal ushort Type;
        public override string ToString() => $"[c/74667a:{Name}] ([c/74667a:{Type}])";
    }
}
