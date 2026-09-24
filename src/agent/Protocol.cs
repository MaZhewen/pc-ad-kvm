using System;

namespace PcKvm
{
    /// <summary>协议编解码。纯函数，不做任何 I/O，便于单独推理与测试。</summary>
    public static class Protocol
    {
        public const byte MsgPointer = 0x0B;
        public const byte MsgGeometry = 0x0C;
        public const byte MsgPointerReady = 0x0D;
        public const byte MsgPointerAck = 0x0E;
        public static byte[] EncodeGeometry(uint epoch, ushort width, ushort height, byte rotation)
        {
            byte[] b = new byte[10]; b[0] = MsgGeometry;
            PutU32(b, 1, epoch); PutU16(b, 5, width); PutU16(b, 7, height); b[9] = rotation; return b;
        }
        public static byte[] EncodePointer(uint epoch, uint sequence, ushort x, ushort y)
        {
            byte[] b = new byte[13]; b[0] = MsgPointer;
            PutU32(b, 1, epoch); PutU32(b, 5, sequence);
            PutU16(b, 9, x); PutU16(b, 11, y); return b;
        }
        public const byte MsgEnter  = 0x01;   // int16 x, int16 y
        public const byte MsgMove   = 0x02;   // int16 dx, int16 dy
        public const byte MsgButton = 0x03;   // uint8 btn, uint8 down
        public const byte MsgScroll = 0x04;   // int16 dx, int16 dy
        public const byte MsgKey    = 0x05;   // uint16 scancode, uint8 down, uint8 mods
        public const byte MsgLeave  = 0x06;   // 无负载
        public const byte MsgConfig = 0x07;   // uint8 edge, uint16 pcEdgeLen, uint16 phoneW, uint16 phoneH
        public const byte MsgPing   = 0x08;   // uint32 seq
        public const byte MsgPong   = 0x09;   // uint32 seq
        public const byte MsgHome   = 0x0A;   // 无负载：让设备把光标硬顶到 (0,0)

        public static byte[] EncodeEnter(short x, short y)
        {
            byte[] b = new byte[5];
            b[0] = MsgEnter;
            PutI16(b, 1, x); PutI16(b, 3, y);
            return b;
        }

        public static byte[] EncodeMove(short dx, short dy)
        {
            byte[] b = new byte[5];
            b[0] = MsgMove;
            PutI16(b, 1, dx); PutI16(b, 3, dy);
            return b;
        }

        public static byte[] EncodeButton(byte btn, byte down)
        {
            byte[] b = new byte[3];
            b[0] = MsgButton; b[1] = btn; b[2] = down;
            return b;
        }

        public static byte[] EncodeScroll(short dx, short dy)
        {
            byte[] b = new byte[5];
            b[0] = MsgScroll;
            PutI16(b, 1, dx); PutI16(b, 3, dy);
            return b;
        }

        public static byte[] EncodeKey(ushort scancode, byte down, byte mods)
        {
            byte[] b = new byte[5];
            b[0] = MsgKey;
            PutU16(b, 1, scancode); b[3] = down; b[4] = mods;
            return b;
        }

        public static byte[] EncodeLeave()
        {
            return new byte[] { MsgLeave };
        }

        public static byte[] EncodeConfig(byte edge, ushort pcEdgeLen, ushort phoneW, ushort phoneH)
        {
            byte[] b = new byte[8];
            b[0] = MsgConfig; b[1] = edge;
            PutU16(b, 2, pcEdgeLen); PutU16(b, 4, phoneW); PutU16(b, 6, phoneH);
            return b;
        }

        public static byte[] EncodeHome()
        {
            return new byte[] { MsgHome };
        }

        public static byte[] EncodePing(uint seq) { return EncodeU32(MsgPing, seq); }
        public static byte[] EncodePong(uint seq) { return EncodeU32(MsgPong, seq); }

        static byte[] EncodeU32(byte type, uint v)
        {
            byte[] b = new byte[5];
            b[0] = type;
            PutU32(b, 1, v);
            return b;
        }

        /// <summary>所有消息都是定长的，按 type 查表得长度；返回 0 表示类型未知。</summary>
        public static int PayloadLength(byte type)
        {
            switch (type)
            {
                case MsgPointer: return 12;
                case MsgGeometry: return 9;
                case MsgPointerReady: return 4;
                case MsgPointerAck: return 12;
                case MsgEnter: return 4;
                case MsgMove: return 4;
                case MsgButton: return 2;
                case MsgScroll: return 4;
                case MsgKey: return 4;
                case MsgLeave: return 0;
                case MsgConfig: return 7;
                case MsgPing: return 4;
                case MsgPong: return 4;
                case MsgHome: return 0;
                default: return -1;
            }
        }

        public static short GetI16(byte[] b, int off)
        {
            return (short)(b[off] | (b[off + 1] << 8));
        }

        public static ushort GetU16(byte[] b, int off)
        {
            return (ushort)(b[off] | (b[off + 1] << 8));
        }

        public static uint GetU32(byte[] b, int off)
        {
            return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        }

        static void PutI16(byte[] b, int off, short v) { PutU16(b, off, (ushort)v); }
        static void PutU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }
        static void PutU32(byte[] b, int off, uint v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
