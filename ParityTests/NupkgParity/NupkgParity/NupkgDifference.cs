using System;
using System.Collections.Generic;

namespace NupkgParity
{
    public class NupkgDifference
    {
        public string Test { get; set; }

        public string Project { get; set; }

        public string V2Framework { get; set; }

        public string V3Framework { get; set; }

        public string Package { get; set; }

        public override string ToString()
        {
            return String.Format("{0},{1},{2},{3},{4}", Test, Project, V2Framework, V3Framework, Package);
        }
    }
}