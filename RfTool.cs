using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RfTool", "Pinguin", "0.1.0")]
    [Description("Adds console commands to manipulate in-game RF objects")]
    class RfTool : CovalencePlugin
    {
        private const string version = "0.1.0";
        private const int frequency_min = 0;
        private const int frequency_reserved_min = 4760;
        private const int frequency_reserved_max = 4770;
        private const int frequency_max = 9999;

        /*
         * List all registered listeners / broadcasters
         */
        [Command("rftool.inspect"), Permission("rftool.use")]
        void RfToolInspect(IPlayer player, string command, string[] args)
        {
            for (int cur_freq = 1; cur_freq < frequency_max; cur_freq++)
            {
                var listeners = RFManager.GetListenList(cur_freq);
                if(listeners.Count>0)
                {
                    player.Reply("RfTool v" + version + " :: Found " + listeners.Count.ToString() + " listener(s) on frequency " + cur_freq.ToString());
                }
            }
            for (int cur_freq = 1; cur_freq < frequency_max; cur_freq++)
            {
                var broadcasters = RFManager.GetBroadcasterList(cur_freq);
                if (broadcasters.Count > 0)
                {
                    player.Reply("RfTool v" + version + " :: Found " + broadcasters.Count.ToString() + " broadcaster(s) on frequency " + cur_freq.ToString());
                }
            }
        }

        /*
         * Enable all RF listeners on a given frequency
         */
        [Command("rftool.enable"), Permission("rftool.use")]
        void RfToolEnable(IPlayer player, string command, string[] args)
        {
            int frequency = 0;

            // Get & check frequency
            frequency = this.GetFrequency(player, args);
            if (frequency == 0) return;

            // Get all listeners for given frequency and enable them
            var listeners = RFManager.GetListenList(frequency);
            for (int i=0; i<listeners.Count; i++)
            {
                listeners[i].RFSignalUpdate(true);
            }

            player.Reply("RfTool v" + version + " :: Enabled " + listeners.Count .ToString() + " listener(s) on frequency " + frequency.ToString());
        }

        /*
         * Disable all RF listeners on a given frequency
         */
        [Command("rftool.disable"), Permission("rftool.use")]
        void RfToolDisable(IPlayer player, string command, string[] args)
        {
            int frequency = 0;

            // Get & check frequency
            frequency = this.GetFrequency(player, args);
            if (frequency == 0) return;

            // Get all listeners for given frequency and disable them
            var listeners = RFManager.GetListenList(frequency);
            for (int i = 0; i < listeners.Count; i++)
            {
                listeners[i].RFSignalUpdate(false);
            }

            player.Reply("RfTool v" + version + " :: Disabled " + listeners.Count.ToString() + " listener(s) on frequency " + frequency.ToString());
        }

        /*
         * Helper function to parse and check a given frequency
         */
        private int GetFrequency(IPlayer player, string[] args)
        {
            // Check if player specified a frequency
            if (args.Length == 0 || args.Length > 1)
            {
                player.Reply("RfTool v" + version + " :: Usage: rftool.[enable|disable] <frequency>");
                return 0;
            }

            // Check if player specified a correct frequency
            int frequency;
            if (!int.TryParse(args[0], out frequency))
            {
                player.Reply("RfTool v" + version + " :: Invalid frequency specified!");
                return 0;
            }

            // Make sure frequency is valid
            if (frequency <= frequency_min || (frequency >= frequency_reserved_min && frequency <= frequency_reserved_max) || frequency > frequency_max)
            {
                player.Reply("RfTool v" + version + " :: Specified frequency is reserved or out of bounds!");
                return 0;
            }

            return frequency;
        }
    }
}
