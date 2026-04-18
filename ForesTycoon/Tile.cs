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
            low = this.n.W;

            if (this.e.W < this.low) low = this.e.W;
            if (this.s.W < this.low) low = this.s.W;
            if (this.w.W < this.low) low = this.w.W;

            int ni = this.n.W - low;
            int ei = this.e.W - low;
            int si = this.s.W - low;
            int wi = this.w.W - low;

            this.code = ni.ToString() + ei.ToString() + si.ToString() + wi.ToString();

            return (code);
        }

    }
}
