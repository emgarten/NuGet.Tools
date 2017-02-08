using NuGet.Test.Utility;

namespace TestProjectGraphGenerator
{
    public class ProjectNode
    {
        public SimpleTestProjectContext Project { get; set; }

        /// <summary>
        /// 0 is the root
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// Position to the right. 0 is the left edge.
        /// </summary>
        public int Column { get; set; }

        public string Name
        {
            get
            {
                return $"D{Depth}C{Column}";
            }
        }
    }
}
