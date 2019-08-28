/*
    RfTool - An umod plugin to manipulate/intercept in-game RF objects/signals
    Copyright (C) 2019 by Pinguin

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("RfTool", "Pinguin", "0.2.0")]
    [Description("Adds console commands to manipulate/intercept in-game RF objects/signals")]
    class RfTool : CovalencePlugin
    {
        private const string version = "0.2.0";
        private const int frequency_min = 0;
        private const int frequency_max = 9999;
        private bool rftool_debug = false;
        private RfToolConfig config_data;
        private Timer listener_timer;

        #region Plugin Config
        /*
         * Classes & functions to load / store plugin configuration
         */
        private struct ListenerData
        {
            public int frequency;
            public string msg;
            public int block_ticks;
            public int cur_block_ticks;

            public ListenerData(int _frequency, string _msg, int _block_ticks)
            {
                frequency = _frequency;
                msg = _msg;
                block_ticks = _block_ticks;
                cur_block_ticks = 0;
            }
        }
        private class RfToolConfig
        {
            public float tick_interval = 1f;
            public List<ListenerData> configured_listeners = new List<ListenerData>();
        }
        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true);
        }
        private RfToolConfig GetDefaultConfig()
        {
            this.Log("Creating new configuration file with default values");
            return new RfToolConfig();
        }
        #endregion

        #region Umod Hooks
        /*
         * Initialize plugin after it got loaded
         */
        private void Loaded()
        {
            // Load plugin config
            this.config_data = Config.ReadObject<RfToolConfig>();

            // All configured listeners should be unblocked at the start
            this.ResetListenersCurBlockTick();

            // Start listener loop timer
            this.listener_timer = timer.Every(this.config_data.tick_interval, this.CheckListeners);
        }

        /*
         * Clear up on unload
         */
        private void Unload()
        {
            // Stop listener loop timer
            this.listener_timer.Destroy();
        }
        #endregion

        #region Console Commands
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
                    this.ReplyToPlayer(player, "Found " + listeners.Count.ToString() + " listener(s) on frequency " + cur_freq.ToString());
                }
            }
            for (int cur_freq = 1; cur_freq < frequency_max; cur_freq++)
            {
                var broadcasters = RFManager.GetBroadcasterList(cur_freq);
                if (broadcasters.Count > 0)
                {
                    this.ReplyToPlayer(player, "Found " + broadcasters.Count.ToString() + " broadcaster(s) on frequency " + cur_freq.ToString());
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
            for (int i=0; i < listeners.Count; i++)
            {
                listeners[i].RFSignalUpdate(true);
            }

            this.ReplyToPlayer(player, "Enabled " + listeners.Count .ToString() + " listener(s) on frequency " + frequency.ToString());
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

            this.ReplyToPlayer(player, "Disabled " + listeners.Count.ToString() + " listener(s) on frequency " + frequency.ToString());
        }

        /*
         * Start listening on specific frequency and log broadcasts
         */
        [Command("rftool.listeners.add"), Permission("rftool.use")]
        void RfToolListenersAdd(IPlayer player, string command, string[] args)
        {
            int frequency=0;
            int block_ticks = 0;

            // Check command line args
            if (args.Length == 0 || args.Length > 3)
            {
                this.ReplyToPlayer(player, "Usage: rftool.listeners.add <frequency> <log_message> [<block_delay>]");
                return;
            }
            if (!int.TryParse(args[0], out frequency))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return;
            }
            if (args.Length == 3 && !int.TryParse(args[2], out block_ticks))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return;
            }
            if (frequency <= frequency_min || frequency > frequency_max)
            {
                this.ReplyToPlayer(player, "Specified frequency is out of bounds!");
                return;
            }

            // Make sure we are not already listening on that frequency
            if(IsListenerConfigured(frequency))
            {
                this.ReplyToPlayer(player, "A listener on frequency " + frequency.ToString() + " is already configured!");
                return;
            }

            // Add and save listener for given frequency
            this.config_data.configured_listeners.Add(new ListenerData(frequency, args[1], block_ticks));
            Config.WriteObject<RfToolConfig>(this.config_data, true);
            this.ReplyToPlayer(player, "Added listener on frequency " + frequency.ToString() + " with " + block_ticks.ToString() + " block tick(s) and log message '" + args[1] + "'");
        }

        /*
         * Stop listening on specific frequency and log broadcasts
         */
        [Command("rftool.listeners.del"), Permission("rftool.use")]
        void RfToolListenersDel(IPlayer player, string command, string[] args)
        {
            int frequency = 0;

            // Check command line args
            if (args.Length != 1)
            {
                this.ReplyToPlayer(player, "Usage: rftool.listeners.del <frequency>");
                return;
            }
            if (!int.TryParse(args[0], out frequency))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return;
            }
            if (frequency <= frequency_min || frequency > frequency_max)
            {
                this.ReplyToPlayer(player, "Specified frequency is out of bounds!");
                return;
            }

            // Make sure we are listening on that frequency
            if (!IsListenerConfigured(frequency))
            {
                this.ReplyToPlayer(player, "Currently no listeners on frequency " + frequency.ToString() + " configured!");
                return;
            }

            // Remove listener for given frequency
            for (int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                if (this.config_data.configured_listeners[i].frequency == frequency)
                {
                    this.config_data.configured_listeners.RemoveAt(i);
                    Config.WriteObject<RfToolConfig>(this.config_data, true);
                    this.ReplyToPlayer(player, "Removed listener on frequency " + frequency.ToString());
                    break;
                }
            }
        }

        /*
         * List all configured listeners
         */
        [Command("rftool.listeners.list"), Permission("rftool.use")]
        void RfToolListenersList(IPlayer player, string command, string[] args)
        {
            // Make sure there are listeners configured
            if(this.config_data.configured_listeners.Count==0)
            {
                this.ReplyToPlayer(player, "Currently no listeners configured");
                return;
            }

            // Show a list of configured listeners
            this.ReplyToPlayer(player, "The following " + this.config_data.configured_listeners.Count.ToString() + " listener(s) is/are currently configured (Frequency | Log message | Block ticks):");
            for (int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                this.ReplyToPlayer(player, this.config_data.configured_listeners[i].frequency.ToString() + " | '" + this.config_data.configured_listeners[i].msg + "' | " + this.config_data.configured_listeners[i].block_ticks.ToString());
            }
        }

        /*
         * Change interval time of listener loop
         */
        [Command("rftool.listeners.set_interval"), Permission("rftool.use")]
        void RfToolListenersSetInterval(IPlayer player, string command, string[] args)
        {
            float interval = 0f;

            // Check command line args
            if (args.Length != 1)
            {
                this.ReplyToPlayer(player, "Usage: rftool.listeners.set_interval <secs>");
                return;
            }
            if (!float.TryParse(args[0], out interval) || interval == 0f)
            {
                this.ReplyToPlayer(player, "Invalid interval value specified!");
                return;
            }

            // Make sure player actually specified a new value
            if(this.config_data.tick_interval == interval)
            {
                this.ReplyToPlayer(player, "Current listeners tick interval is already set to " + this.config_data.tick_interval.ToString() + " seconds");
                return;
            }

            // Stop current timer loop
            this.listener_timer.Destroy();

            // Reset current block ticks
            this.ResetListenersCurBlockTick();

            // Change interval and update config file
            this.config_data.tick_interval = interval;
            Config.WriteObject<RfToolConfig>(this.config_data);

            // Start new timer with new interval
            this.listener_timer = timer.Every(this.config_data.tick_interval, this.CheckListeners);

            this.ReplyToPlayer(player, "Updated listeners tick interval to " + interval.ToString() + " seconds");
        }

        /*
         * Get interval time of listener loop
         */
        [Command("rftool.listeners.get_interval"), Permission("rftool.use")]
        void RfToolListenersGetInterval(IPlayer player, string command, string[] args)
        {
            this.ReplyToPlayer(player, "Current listeners tick interval is set to " + this.config_data.tick_interval.ToString() + " seconds");
        }
        #endregion

        #region Listener Callback
        /*
         * Callback function getting called by the listener loop timer
         */
        private void CheckListeners()
        {
            ListenerData listener_data;

            // Iterate over every configured listener
            for (int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                // Get listener data
                listener_data = this.config_data.configured_listeners[i];

                // Make sure listener is currently not blocked
                if (listener_data.cur_block_ticks == 0)
                {
                    // Check if broadcasters on the listeners configured frequency are currently active
                    this.LogDebug("Checking for broadcasters on frequency " + listener_data.frequency.ToString());
                    var broadcasters = RFManager.GetBroadcasterList(listener_data.frequency);
                    if (broadcasters.Count > 0)
                    {
                        this.LogDebug("Found " + broadcasters.Count.ToString() + " broadcaster on frequency " + listener_data.frequency.ToString());
                        this.Log(listener_data.msg);
                        if (listener_data.block_ticks != 0)
                        {
                            // Set block ticks for next loop and save values back (shitty C#)
                            listener_data.cur_block_ticks = this.config_data.configured_listeners[i].block_ticks;
                            this.config_data.configured_listeners[i] = listener_data;
                        }
                    }
                }
                else
                {
                    // Decrement block ticks and save values back (shitty C#)
                    listener_data.cur_block_ticks--;
                    this.config_data.configured_listeners[i] = listener_data;
                }
            }
        }
        #endregion

        #region Helper Functions
        /*
         * Helper function to parse and check a given frequency
         */
        private int GetFrequency(IPlayer player, string[] args)
        {
            // Check if player specified a frequency
            if (args.Length != 1)
            {
                this.ReplyToPlayer(player, "Usage: rftool.[enable|disable] <frequency>");
                return 0;
            }

            // Check if player specified a correct frequency
            int frequency;
            if (!int.TryParse(args[0], out frequency))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return 0;
            }

            // Make sure frequency is valid
            if (frequency <= frequency_min || frequency > frequency_max)
            {
                this.ReplyToPlayer(player, "Specified frequency is out of bounds!");
                return 0;
            }

            return frequency;
        }

        /*
         * Helper function to check if a listener for a certain frequency has already been configured
         */
        private bool IsListenerConfigured(int frequency)
        {
            for(int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                if (this.config_data.configured_listeners[i].frequency == frequency) return true;
            }
            return false;
        }

        /*
         * Helper function to reset all configured listeners current block tick
         */
        private void ResetListenersCurBlockTick()
        {
            ListenerData listener_data;

            // All configured listener's cur_block_ticks shell start at 0
            for (int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                if (this.config_data.configured_listeners[i].cur_block_ticks != 0)
                {
                    // Set current block ticks to 0 (shitty C#)
                    listener_data = this.config_data.configured_listeners[i];
                    listener_data.cur_block_ticks = 0;
                    this.config_data.configured_listeners[i] = listener_data;
                }
            }
        }

        /*
         * Helper functions to send messages to players / console
         */
        private void ReplyToPlayer(IPlayer player, string msg)
        {
            player.Reply("RfTool v" + version + " :: " + msg);
        }
        private void Log(string msg)
        {
            Puts("RfTool v" + version + " :: " + msg);
        }
        private void LogDebug(string msg)
        {
            if(this.rftool_debug)
            {
                this.Log("DEBUG :: " + msg);
            }
        }
        #endregion
    }
}
