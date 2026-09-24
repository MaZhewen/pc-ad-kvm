using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PcKvm;

class AbsoluteIntegration
{
    static void Put(byte[] b, int o, uint v) { b[o]=(byte)v; b[o+1]=(byte)(v>>8); b[o+2]=(byte)(v>>16); b[o+3]=(byte)(v>>24); }
    static void Put16(byte[] b, int o, ushort v) { b[o]=(byte)v; b[o+1]=(byte)(v>>8); }
    static void ReadExact(Stream s, byte[] b) { int n=0; while(n<b.Length) { int r=s.Read(b,n,b.Length-n); if(r<=0) throw new Exception("injector disconnected"); n+=r; } }
    static void Main()
    {
        TcpListener l=new TcpListener(IPAddress.Loopback,27183);
        try
        {
            try { l.Start(); }
            catch (SocketException e) { Console.WriteLine("SKIP listen: "+e.SocketErrorCode); return; }
            Console.WriteLine("LISTENING"); Console.Out.Flush();
            using(TcpClient c=l.AcceptTcpClient()) using(NetworkStream s=c.GetStream())
            {
                byte[] geometry=new byte[10]; geometry[0]=Protocol.MsgGeometry; Put(geometry,1,1); Put16(geometry,5,3200); Put16(geometry,7,2136); geometry[9]=90; s.Write(geometry,0,geometry.Length);
                byte[] head=new byte[1]; ReadExact(s,head); if(head[0]!=Protocol.MsgPointerReady) throw new Exception("expected pointer ready, got "+head[0]);
                byte[] ready=new byte[4]; ReadExact(s,ready); if(ready[0]!=1) throw new Exception("wrong epoch");
                byte[] pointer=Protocol.EncodePointer(1,1,800,800); s.Write(pointer,0,pointer.Length);
                ReadExact(s,head); if(head[0]!=Protocol.MsgPointerAck) throw new Exception("expected pointer ack, got "+head[0]);
                byte[] ack=new byte[12]; ReadExact(s,ack); if(Protocol.GetU32(ack,0)!=1 || Protocol.GetU32(ack,4)!=1) throw new Exception("wrong ack");
                Console.WriteLine("POINTER_ACK"); Console.Out.Flush();
                Thread.Sleep(2000);
            }
            Console.WriteLine("PASS production absolute protocol");
        }
        catch (Exception e) { Console.WriteLine("FAIL integration: "+e.Message); }
        finally { try { l.Stop(); } catch (Exception) { } }
    }
}
