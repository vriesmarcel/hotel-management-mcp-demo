using System;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Windows.Forms;
using ModelContextProtocol.Server;
using Project_HMS.Mcp;

namespace Project_HMS
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                // A second entry point in the same legacy app. No service extraction.
                var tools = new AvailabilityTools();
                var tool = McpServerTool.Create(typeof(AvailabilityTools).GetMethod(nameof(AvailabilityTools.GetAvailableRooms)), tools);
                using (var mcp = new EmbeddedMcpHost(tool))
                {
                    mcp.Activity += message => Trace.TraceInformation(message);
                    mcp.Start();
                    var login = new Login();
                    login.Text += " - MCP: " + mcp.Endpoint + " (PID " + Process.GetCurrentProcess().Id + ")";
                    Application.Run(login);
                }
            }
            catch (HttpListenerException)
            {
                MessageBox.Show("MCP could not start. Close the other hotel demo using port 5057. See DEMO.md for URL permissions.");
            }
            catch (ConfigurationErrorsException)
            {
                MessageBox.Show("Run scripts/Configure-Database.ps1 once to configure the local database, then press F5.");
            }
        }
    }
}
