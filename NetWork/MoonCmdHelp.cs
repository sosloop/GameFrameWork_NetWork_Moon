using System;
using System.Collections.Generic;
using GameFramework;

namespace Moon
{
    public static class MoonCmdHelp
    {
        public static readonly Dictionary<Type, ushort> OpcodeTypes = new Dictionary<Type,ushort>();
        public static readonly Dictionary<ushort,Type> TypeOpcodes = new Dictionary<ushort,Type>();
        
        public static readonly Dictionary<ushort,string> OpcodeNames = new Dictionary<ushort,string>();
        public static readonly Dictionary<string,ushort> NameOpcodes = new Dictionary<string,ushort>();
        
        public static readonly Dictionary<CmdCode,ushort> CmdOpCodes = new Dictionary<CmdCode,ushort>();
        public static readonly Dictionary<ushort,CmdCode> OpCodeCmds = new Dictionary<ushort,CmdCode>();
        
        
        static MoonCmdHelp()
        {
            foreach (CmdCode cmdCode in Enum.GetValues(typeof(CmdCode)))
            {
                ushort cmdInt = (ushort) cmdCode;
                string enumName = Enum.GetName(typeof(CmdCode), cmdCode);
                // type
                string messageName = "NetMessage." + enumName;
                Type type = Utility.Assembly.GetType(messageName);
                //
                OpcodeTypes.Add(type,cmdInt);
                TypeOpcodes.Add(cmdInt,type);

                //
                OpcodeNames.Add(cmdInt,enumName);
                NameOpcodes.Add(enumName,cmdInt);
                
                //
                CmdOpCodes.Add(cmdCode,cmdInt);
                OpCodeCmds.Add(cmdInt,cmdCode);
            }
        }

        public static ushort GetOpCode(this string self)
        {
            return NameOpcodes[self];
        }

        public static ushort GetOpCode(this CmdCode self)
        {
            return CmdOpCodes[self];
        }
        
        public static CmdCode GetCmdCode(this ushort self)
        {
            return OpCodeCmds[self];
        }
        
        public static string GetCmdCodeName(this ushort self)
        {
            return OpcodeNames[self];
        }

    }
}