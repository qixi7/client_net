using System;
using System.Collections.Generic;

namespace Public.Log
{
    public class LogHelper
    {
        public enum LevelEnum
        {
            Debug = 0,    // 调试
            Info = 1,     // 信息
            Warn = 2,     // 警告
            Error = 3,    // 错误
        }

        private static LevelEnum LogLevel = 0;    // 日志等级. 默认打开所有日志

        // 设置日志等级
        public static void SetLogLevel(LevelEnum lv)
        {
            LogLevel = lv;
        }

        private static readonly Dictionary<LevelEnum, string> Level2Str = new Dictionary<LevelEnum, string>
        {
            {LevelEnum.Debug, "[Debug]: "},
            {LevelEnum.Info, "[Info]: "},
            {LevelEnum.Warn, "[Warning]: "},
            {LevelEnum.Error, "[Error]: "},
        };
        
        public static List<string> Logs = new List<string>();   //缓存所有日志，用于上传查bug

        public static void DebugF(string format, params object[] arg)
        {
            if (LogLevel > LevelEnum.Debug)
            {
                return;
            }
//            UnityEngine.Debug.LogFormat(Level2Str[LevelEnum.Debug]+format, arg);
//            Logs.Add(String.Format(Level2Str[LevelEnum.Debug]+format, arg));
            Console.WriteLine(Level2Str[LevelEnum.Debug]+format, arg);
        }

        public static void InfoF(string format, params object[] arg)
        {
            if (LogLevel > LevelEnum.Info)
            {
                return;
            }
            Console.WriteLine(Level2Str[LevelEnum.Info]+format, arg);
//            UnityEngine.Debug.LogFormat(Level2Str[LevelEnum.Info]+format, arg);
//            Logs.Add(String.Format(Level2Str[LevelEnum.Info]+format, arg));
        }

        public static void WarnF(string format, params object[] arg)
        {
            if (LogLevel > LevelEnum.Warn)
            {
                return;
            }
            Console.WriteLine(Level2Str[LevelEnum.Warn]+format, arg);
//            UnityEngine.Debug.LogWarningFormat(Level2Str[LevelEnum.Warn]+format, arg);
//            Logs.Add(String.Format(Level2Str[LevelEnum.Warn]+format, arg));
        }

        public static void ErrorF(string format, params object[] arg)
        {
            if (LogLevel > LevelEnum.Error)
            {
                return;
            }
            Console.WriteLine(Level2Str[LevelEnum.Error]+format, arg);
//            UnityEngine.Debug.LogErrorFormat(Level2Str[LevelEnum.Error]+format, arg);
//            Logs.Add(String.Format(Level2Str[LevelEnum.Error]+format, arg));
        }
    }
}
