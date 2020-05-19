using System.Collections.Generic;

namespace BlocksRegenerator
{
    public struct BRConfig
    {
        public bool status { get; set;  }
        public bool ping { get; set; }
        public List<ushort> tiles { get; set; }
        public short time { get; set; }
    }
}
