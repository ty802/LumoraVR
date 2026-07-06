

using System;
using System.IO;

namespace Lumora.Core.Persistence;
public static class PathResolver {
    private static bool initialized = false;
    public static string LocalPath { get; private set; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingPath { get; private set; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string CachePath { get; private set; } = 
    #if LINUX 
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
    #elif OSX
        Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
    #else
        LocalPath;
    #endif
    public static void initialize(String localPath,String roamingPath,String cachePath){
        if(initialized) throw new Exception("you cant initilize the path resolver more then once");
        LocalPath = localPath;
        RoamingPath = roamingPath;
        CachePath = cachePath;
        initialized = true;
    }
}