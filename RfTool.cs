/*
    RfTool - A Rust umod plugin to manipulate/intercept in-game RF objects/signals
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
    [Info("RfTool", "Pinguin", "0.2.1")]
    [Description("A Rust umod plugin to manipulate/intercept in-game RF objects/signals")]
    class RfTool : CovalencePlugin
    {
        private const string version = "0.2.1";
        private int frequency_min = RFManager.minFreq; // 1
        private int frequency_max = RFManager.maxFreq; // 9999
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
            public int block_delay;
            public int cur_block_delay;

            public ListenerData(int _frequency, string _msg, int _block_delay)
            {
                frequency = _frequency;
                msg = _msg;
                block_delay = _block_delay;
                cur_block_delay = 0;
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
            this.ResetListenersCurBlockDelay();

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
         * Print available commands and a short description
         */
        [Command("rftool")]
        void RfToolHelp(IPlayer player, string command, string[] args)
        {
            this.ReplyToPlayer(player, "RfTool - A Rust umod plugin to manipulate/intercept in-game RF objects/signals");
            this.ReplyToPlayer(player, "Copyright(C) 2019 by Pinguin and released under GPLv3");
            this.ReplyToPlayer(player, "");
            this.ReplyToPlayer(player, "The following commands are available:");
            this.ReplyToPlayer(player, "  rftool.inspect : List all currently in-game exisiting receivers / broadcasters and their frequencies.");
            this.ReplyToPlayer(player, "  rftool.enable : Enable all in-game receivers on the specified frequency.");
            this.ReplyToPlayer(player, "  rftool.disable : Disable all in-game receivers on the specified frequency.");
            this.ReplyToPlayer(player, "  rftool.listeners.list : List all configured virtual receivers.");
            this.ReplyToPlayer(player, "  rftool.listeners.add : Add a virtual receiver on the specified frequency.");
            this.ReplyToPlayer(player, "  rftool.listeners.del : Delete the virtual receiver on the specified frequency.");
            this.ReplyToPlayer(player, "  rftool.listeners.get_interval : Get the interval in which virtual receivers operate.");
            this.ReplyToPlayer(player, "  rftool.listeners.set_interval : Set the interval in which virtual receivers operate.");
            this.ReplyToPlayer(player, "");
            this.ReplyToPlayer(player, "For commands that take arguments, more help is available by executing them without any arguments.");
            this.ReplyToPlayer(player, "");
            this.ReplyToPlayer(player, "To be able to execute any rftool commands, you need to have the umod 'rftool.use' right assigned to your user.");
        }
        /*
         * List all registered listeners / broadcasters
         */
        [Command("rftool.inspect"), Permission("rftool.use")]
        void RfToolInspect(IPlayer player, string command, string[] args)
        {
            for (int cur_freq = frequency_min; cur_freq <= frequency_max; cur_freq++)
            {
                var listeners = RFManager.GetListenList(cur_freq);
                if(listeners.Count>0)
                {
                    this.ReplyToPlayer(player, "Found " + listeners.Count.ToString() + " listener(s) on frequency " + cur_freq.ToString());
                }
            }
            for (int cur_freq = frequency_min; cur_freq <= frequency_max; cur_freq++)
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

            // Check if player specified a frequency
            if (args.Length != 1)
            {
                this.ReplyToPlayer(player, "Usage:");
                this.ReplyToPlayer(player, "  rftool.enable <frequency>");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Description:");
                this.ReplyToPlayer(player, "  This command enables all currently available in-game receivers listening on the specified");
                this.ReplyToPlayer(player, "  frequency <frequency>. This is similiar to broadcasting on the given frequency in-game");
                this.ReplyToPlayer(player, "  except that receivers set to the given frequency after this command was executed won't");
                this.ReplyToPlayer(player, "  get enabled!");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Options:");
                this.ReplyToPlayer(player, "  <frequency> : The frequency on which in-game receivers have to listen to get enabled (" + frequency_min.ToString() + "-" + frequency_max.ToString() + ").");
                return;
            }

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

            // Check if player specified a frequency
            if (args.Length != 1)
            {
                this.ReplyToPlayer(player, "Usage:");
                this.ReplyToPlayer(player, "  rftool.disable <frequency>");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Description:");
                this.ReplyToPlayer(player, "  This command disables all currently available in-game receivers listening on the specified");
                this.ReplyToPlayer(player, "  frequency <frequency>.");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Options:");
                this.ReplyToPlayer(player, "  <frequency> : The frequency on which in-game receivers have to listen to get disabled (" + frequency_min.ToString() + "-" + frequency_max.ToString() + ").");
                return;
            }

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
            int block_delay = 0;

            // Check command line args
            if (args.Length == 0 || args.Length > 3)
            {
                this.ReplyToPlayer(player, "Usage:");
                this.ReplyToPlayer(player, "  rftool.listeners.add <frequency> <log_message> [<block_delay>]");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Description:");
                this.ReplyToPlayer(player, "  This command adds a new virtual RF receiver that, when triggered in-game");
                this.ReplyToPlayer(player, "  on the specified frequency <frequency>, will log the specified <log_message>");
                this.ReplyToPlayer(player, "  message to the console. Optionally, a block delay <block_delay> may be specified");
                this.ReplyToPlayer(player, "  during which no more messages should be send to the console once triggered. The");
                this.ReplyToPlayer(player, "  actual delay is <block_delay> * 'configured interval'. See also the help message");
                this.ReplyToPlayer(player, "  of the command rftool.listeners.set_interval");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Options:");
                this.ReplyToPlayer(player, "  <frequency> : The frequency on which to listen for broadcasts (" + frequency_min.ToString() + "-" + frequency_max.ToString() + ").");
                this.ReplyToPlayer(player, "  <log_message> : The message that should be logged to the console.");
                this.ReplyToPlayer(player, "  <block_delay> : Delay to block once triggered (0=no delay).");
                return;
            }
            if (!int.TryParse(args[0], out frequency))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return;
            }
            if (args.Length == 3 && !int.TryParse(args[2], out block_delay))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return;
            }
            if (frequency < frequency_min || frequency > frequency_max)
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
            this.config_data.configured_listeners.Add(new ListenerData(frequency, args[1], block_delay));
            Config.WriteObject<RfToolConfig>(this.config_data, true);
            this.ReplyToPlayer(player, "Added listener on frequency " + frequency.ToString() + " with a block delay of " + block_delay.ToString() + " and the log message '" + args[1] + "'");
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
                this.ReplyToPlayer(player, "Usage:");
                this.ReplyToPlayer(player, "  rftool.listeners.del <frequency>");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Description:");
                this.ReplyToPlayer(player, "  This command removes a previously added virtual RF receiver operating on the");
                this.ReplyToPlayer(player, "  specified frequency <frequency>.");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Options:");
                this.ReplyToPlayer(player, "  <frequency> : The frequency on which the virtual receiver is listening (" + frequency_min.ToString() + "-" + frequency_max.ToString() + ").");
                return;
            }
            if (!int.TryParse(args[0], out frequency))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return;
            }
            if (frequency < frequency_min || frequency > frequency_max)
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
            this.ReplyToPlayer(player, "The following " + this.config_data.configured_listeners.Count.ToString() + " listener(s) is/are currently configured (Frequency | Log message | Block delay):");
            for (int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                this.ReplyToPlayer(player, this.config_data.configured_listeners[i].frequency.ToString() + " | '" + this.config_data.configured_listeners[i].msg + "' | " + this.config_data.configured_listeners[i].block_delay.ToString());
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
                this.ReplyToPlayer(player, "Usage:");
                this.ReplyToPlayer(player, "  rftool.listeners.set_interval <interval>");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Description:");
                this.ReplyToPlayer(player, "  This command changes the delay in which this plugin will check for broadcasts");
                this.ReplyToPlayer(player, "  to <interval> seconds. A check whether one of the virtual receivers should be");
                this.ReplyToPlayer(player, "  triggered is only carried out once in this interval. This will also control");
                this.ReplyToPlayer(player, "  the final value of the block delay of a triggered receiver. As an example, if");
                this.ReplyToPlayer(player, "  interval is 5 and block delay is 2, a triggered receiver will be silet for the");
                this.ReplyToPlayer(player, "  next 5 * 2 = 10 seconds. See also the help message of the command rftool.listeners.add");
                this.ReplyToPlayer(player, "");
                this.ReplyToPlayer(player, "Options:");
                this.ReplyToPlayer(player, "  <interval> : Interval in seconds (>0).");
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
                this.ReplyToPlayer(player, "Current listeners interval is already set to " + this.config_data.tick_interval.ToString() + " second(s)");
                return;
            }

            // Stop current timer loop
            this.listener_timer.Destroy();

            // Reset current block delay
            this.ResetListenersCurBlockDelay();

            // Change interval and update config file
            this.config_data.tick_interval = interval;
            Config.WriteObject<RfToolConfig>(this.config_data);

            // Start new timer with new interval
            this.listener_timer = timer.Every(this.config_data.tick_interval, this.CheckListeners);

            this.ReplyToPlayer(player, "Updated listeners interval to " + interval.ToString() + " second(s)");
        }

        /*
         * Get interval time of listener loop
         */
        [Command("rftool.listeners.get_interval"), Permission("rftool.use")]
        void RfToolListenersGetInterval(IPlayer player, string command, string[] args)
        {
            this.ReplyToPlayer(player, "Current listeners interval is set to " + this.config_data.tick_interval.ToString() + " second(s)");
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
                if (listener_data.cur_block_delay == 0)
                {
                    // Check if broadcasters on the listeners configured frequency are currently active
                    this.LogDebug("Checking for broadcasters on frequency " + listener_data.frequency.ToString());
                    var broadcasters = RFManager.GetBroadcasterList(listener_data.frequency);
                    if (broadcasters.Count > 0)
                    {
                        this.LogDebug("Found " + broadcasters.Count.ToString() + " broadcaster on frequency " + listener_data.frequency.ToString());
                        this.Log(listener_data.msg);
                        if (listener_data.block_delay != 0)
                        {
                            // Set block delay for next loop and save values back (shitty C#)
                            listener_data.cur_block_delay = this.config_data.configured_listeners[i].block_delay;
                            this.config_data.configured_listeners[i] = listener_data;
                        }
                    }
                }
                else
                {
                    // Decrement block delay and save values back (shitty C#)
                    listener_data.cur_block_delay--;
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
            // Check if player specified a correct frequency
            int frequency;
            if (!int.TryParse(args[0], out frequency))
            {
                this.ReplyToPlayer(player, "Invalid frequency specified!");
                return 0;
            }

            // Make sure frequency is valid
            if (frequency < frequency_min || frequency > frequency_max)
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
         * Helper function to reset all configured listeners current block delay
         */
        private void ResetListenersCurBlockDelay()
        {
            ListenerData listener_data;

            // All configured listener's cur_block_delay shall start at 0
            for (int i = 0; i < this.config_data.configured_listeners.Count; i++)
            {
                if (this.config_data.configured_listeners[i].cur_block_delay != 0)
                {
                    // Set current block delay to 0 (shitty C#)
                    listener_data = this.config_data.configured_listeners[i];
                    listener_data.cur_block_delay = 0;
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
