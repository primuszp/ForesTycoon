using System;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
namespace ForesTycoon
{
    // Embedded, non-interlaced 8-bit RGB/RGBA glTF textures. No platform image API.
    internal readonly record struct PngImage(int Width, int Height, byte[] Pixels)
    {
        internal static PngImage Decode(byte[] png)
        {
            if (png.Length < 33 || !png.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
                throw new InvalidDataException("Invalid PNG.");
            int width=0,height=0,channels=0;
            using var compressed=new MemoryStream();
            for(int offset=8;offset+12<=png.Length;)
            {
                int size=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset,4));
                if(size<0 || size>png.Length-offset-12) throw new InvalidDataException("PNG chunk bounds.");
                string type=System.Text.Encoding.ASCII.GetString(png,offset+4,4);
                if(type=="IHDR") {
                    if(size!=13) throw new InvalidDataException("PNG header.");
                    width=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset+8,4));
                    height=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset+12,4));
                    channels=png[offset+17]==2?3:png[offset+17]==6?4:0;
                    if(width<1||height<1||width>4096||height>4096||channels==0||png[offset+16]!=8||png[offset+20]!=0)
                        throw new InvalidDataException("Only 8-bit non-interlaced RGB/RGBA PNG supported.");
                }
                if(type=="IDAT") compressed.Write(png,offset+8,size);
                offset+=size+12;
            }
            if(channels==0) throw new InvalidDataException("Missing PNG header.");
            compressed.Position=0;
            int stride=checked(width*channels);
            byte[] raw=new byte[checked((stride+1)*height)], pixels=new byte[checked(width*height*4)];
            using(var stream=new ZLibStream(compressed,CompressionMode.Decompress)) stream.ReadExactly(raw);
            byte[] previous=new byte[stride], row=new byte[stride];
            for(int y=0;y<height;y++) {
                int start=y*(stride+1); byte filter=raw[start];
                for(int x=0;x<stride;x++) {
                    int a=x>=channels?row[x-channels]:0,b=previous[x],c=x>=channels?previous[x-channels]:0;
                    int prediction=filter switch {0=>0,1=>a,2=>b,3=>(a+b)/2,4=>Paeth(a,b,c),_=>throw new InvalidDataException("PNG filter.")};
                    row[x]=unchecked((byte)(raw[start+1+x]+prediction));
                }
                for(int x=0;x<width;x++) {
                    int target=(y*width+x)*4,source=x*channels;
                    pixels[target]=row[source]; pixels[target+1]=row[source+1]; pixels[target+2]=row[source+2];
                    pixels[target+3]=channels==4?row[source+3]:(byte)255;
                }
                (row,previous)=(previous,row);
            }
            return new PngImage(width,height,pixels);
        }
        private static int Paeth(int a,int b,int c) {
            int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);
            return pa<=pb&&pa<=pc?a:pb<=pc?b:c;
        }
    }
}
