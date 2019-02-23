using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Polygamy
{
    public class PolyData
    {
        public Dictionary<long, List<string>> PolySpouses;
        public string PrimarySpouse;

        public PolyData()
        {
            PolySpouses = new Dictionary<long, List<string>>();
        }
    }
}
