namespace ForesTycoon
{
    class Tile
    {
        private int id = -100;

        private Node n = null;
        private Node s = null;
        private Node e = null;
        private Node w = null;

        private int low = 0; 

        private string code = "0000";
        private TileShapeInfo shape;

        public Node N
        {
            get { return n; }
            set
            {
                if (value != n)
                {
                    n = value;
                    getCode();
                }
            }
        }

        public Node S
        {
            get { return s; }
            set
            {
                if (value != s)
                {
                    s = value;
                    getCode();
                }
            }
        }

        public Node E
        {
            get { return e; }
            set
            {
                if (value != e)
                {
                    e = value;
                    getCode();
                }
            }
        }

        public Node W
        {
            get { return w; }
            set
            {
                if (value != w)
                {
                    w = value;
                    getCode();
                }
            }
        }

        public int Id
        {
            get { return id; }
            set { id = value; }
        }

        public string Code
        {
            get { return code; }
        }

        public TileShapeInfo Shape
        {
            get { return shape; }
        }

        public int Low
        {
            get { return low; }
        }

        public int LowPos { get; set; }

        public Tile(Node n, Node s, Node e, Node w)
        {
            this.n = n;
            this.s = s;
            this.e = e;
            this.w = w;
            this.code = getCode();
        }

        public string getCode()
        {
            shape = TileShapeInfo.FromCorners(this.w.W, this.s.W, this.e.W, this.n.W);
            low = shape.Min;
            code = shape.RelativeCodeNESW;

            return (code);
        }

    }
}
