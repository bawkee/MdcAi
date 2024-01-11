namespace MdcAi.Extensions.WinUI;

using Microsoft.Extensions.Logging;
using SalaTools.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class ShellUtil
{
    public static void StartUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            typeof(ShellUtil).GetLogger().LogError(ex, "StartUrl");
            throw new("Could not open the URL. App may not have the required system permissions to do this.", ex);            
        }
    }
}