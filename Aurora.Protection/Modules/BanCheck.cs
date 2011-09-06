/*
 *  Copyright 2011 Matthew Beardmore
 *
 *  This file is part of Aurora.Addon.Protection.
 *  Aurora.Addon.Protection is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 *  Aurora.Addon.Protection is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 *  You should have received a copy of the GNU General Public License along with Aurora.Addon.Protection. If not, see http://www.gnu.org/licenses/.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Nini.Config;
using Aurora.Framework;
using Aurora.DataManager;
using OpenMetaverse;
using log4net;
using OpenSim.Services.Interfaces;

namespace Aurora.OptionalModules
{
    #region Grid BanCheck

    public class LoginBanCheck : ILoginModule
    {
        #region Declares

        ILoginService m_service;
        IConfigSource m_source;
        BanCheck m_module;

        #endregion

        #region ILoginModule Members

        public void Initialize(ILoginService service, IConfigSource source, IUserAccountService UASerivce)
        {
            m_source = source;
            m_service = service;
            m_module = new BanCheck(source, UASerivce);
        }

        public bool Login(Hashtable request, UUID User, out string message)
        {
            string ip = (string)request["ip"];
            if (ip == null)
                ip = "";
            string version = (string)request["version"];
            if (version == null)
                version = "";
            string platform = (string)request["platform"];
            if (platform == null)
                platform = "";
            string mac = (string)request["mac"];
            if (mac == null)
                mac = "";
            string id0 = (string)request["id0"];
            if (id0 == null)
                id0 = "";
            return m_module.CheckUser(User, ip,
                version,
                platform,
                mac,
                id0, out message);
        }

        #endregion
    }

    #endregion

    #region BanCheck base

    public class BanCheck
    {
        #region Declares

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IPresenceInfo presenceInfo = null;

        private AllowLevel GrieferAllowLevel = AllowLevel.AllowCleanOnly;
        private IUserAccountService m_accountService = null;
        private List<string> m_bannedViewers = new List<string>();
        private bool m_debug = false;
        private bool m_checkOnLogin = false;
        private bool m_checkOnTimer = true;
        private int TimerMinutes = 60;
        private bool m_enabled = false;

        #endregion

        #region Enums

        public enum AllowLevel : int
        {
            AllowCleanOnly = 0,
            AllowSuspected = 1,
            AllowKnown = 2
        }

        #endregion

        #region Constructor

        public BanCheck (IConfigSource source, IUserAccountService UserAccountService)
        {
            IConfig config = source.Configs["GrieferProtection"];
            if (config == null)
                return;

            m_enabled = config.GetBoolean ("Enabled", true);

            if (!m_enabled)
                return;

            string bannedViewers = config.GetString ("ViewersToBan", "");
            m_bannedViewers = new List<string> (bannedViewers.Split (new string[] { "," }, StringSplitOptions.RemoveEmptyEntries));

            m_checkOnLogin = config.GetBoolean ("CheckForSimilaritiesOnLogin", m_checkOnLogin);
            m_checkOnTimer = config.GetBoolean ("CheckForSimilaritiesOnTimer", m_checkOnTimer);
            TimerMinutes = config.GetInt ("MinutesForTimerToCheck", TimerMinutes);

            if (m_checkOnTimer)
            {
                System.Timers.Timer timer = new System.Timers.Timer (TimerMinutes * 1000 * 60);
                timer.Elapsed += new System.Timers.ElapsedEventHandler (CheckOnTimer);
                timer.Start ();
            }

            GrieferAllowLevel = (AllowLevel)Enum.Parse (typeof (AllowLevel), config.GetString ("GrieferAllowLevel", "AllowKnown"));

            presenceInfo = Aurora.DataManager.DataManager.RequestPlugin<IPresenceInfo> ();
            m_accountService = UserAccountService;

            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Commands.AddCommand(
                    "UserInfo", "UserInfo [UUID] or [First] [Last]", "Info on a given user", UserInfo);
                MainConsole.Instance.Commands.AddCommand(
                    "SetUserInfo", "SetUserInfo [UUID] or [First] [Last] [Flags]", "Sets the info of the given user [Flags]: Clean, Suspected, Known, Banned", SetUserInfo);
                MainConsole.Instance.Commands.AddCommand(
                    "block user", "block [UUID] or [Name]", "Blocks a given user from connecting anymore", BlockUser);
                MainConsole.Instance.Commands.AddCommand(
                    "unblock user", "unblock [UUID] or [Name]", "Removes the block for logging in on a given user", UnBlockUser);
            }
        }

        #endregion

        #region Private and Protected members

        void CheckOnTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            presenceInfo.Check(m_bannedViewers);
        }

        private void CheckForSimilarities(PresenceInfo info)
        {
            presenceInfo.Check(info, m_bannedViewers);
        }

        private PresenceInfo UpdatePresenceInfo(UUID AgentID, PresenceInfo oldInfo, string ip, string version, string platform, string mac, string id0)
        {
            PresenceInfo info = new PresenceInfo();
            info.AgentID = AgentID;
            info.LastKnownIP = ip;
            info.LastKnownViewer = version;
            info.Platform = platform;
            info.LastKnownMac = mac;
            info.LastKnownID0 = id0;

            if (!oldInfo.KnownID0s.Contains(info.LastKnownID0))
                oldInfo.KnownID0s.Add(info.LastKnownID0);
            if (!oldInfo.KnownIPs.Contains(info.LastKnownIP))
                oldInfo.KnownIPs.Add(info.LastKnownIP);
            if (!oldInfo.KnownMacs.Contains(info.LastKnownMac))
                oldInfo.KnownMacs.Add(info.LastKnownMac);
            if (!oldInfo.KnownViewers.Contains(info.LastKnownViewer))
                oldInfo.KnownViewers.Add(info.LastKnownViewer);

            info.KnownViewers = oldInfo.KnownViewers;
            info.KnownMacs = oldInfo.KnownMacs;
            info.KnownIPs = oldInfo.KnownIPs;
            info.KnownID0s = oldInfo.KnownID0s;
            info.KnownAlts = oldInfo.KnownAlts;

            info.Flags = oldInfo.Flags;

            presenceInfo.UpdatePresenceInfo(info);

            return info;
        }

        private PresenceInfo GetInformation(UUID AgentID)
        {
            PresenceInfo oldInfo = presenceInfo.GetPresenceInfo(AgentID);
            if (oldInfo == null)
            {
                PresenceInfo info = new PresenceInfo();
                info.AgentID = AgentID;
                info.Flags = PresenceInfo.PresenceInfoFlags.Clean;
                presenceInfo.UpdatePresenceInfo(info);
                oldInfo = presenceInfo.GetPresenceInfo(AgentID);
            }

            return oldInfo;
        }

        protected void UserInfo(string[] cmdparams)
        {
            UUID AgentID;
            PresenceInfo info;
            if (!UUID.TryParse(cmdparams[1], out AgentID))
            {
                UserAccount account = m_accountService.GetUserAccount(UUID.Zero, cmdparams[1], cmdparams[2]);
                if (account == null)
                {
                    m_log.Warn("Cannot find user.");
                    return;
                }
                AgentID = account.PrincipalID;
            }
            info = GetInformation(AgentID);
            if (info == null)
            {
                m_log.Warn("Cannot find user.");
                return;
            }
            DisplayUserInfo(info);
        }

        protected void BlockUser(string[] cmdparams)
        {
            UUID AgentID;
            PresenceInfo info;
            if (!UUID.TryParse(cmdparams[2], out AgentID))
            {
                UserAccount account = m_accountService.GetUserAccount(UUID.Zero, Util.CombineParams(cmdparams, 2));
                if (account == null)
                {
                    m_log.Warn("Cannot find user.");
                    return;
                }
                AgentID = account.PrincipalID;
            }
            info = GetInformation(AgentID);
            if (info == null)
            {
                m_log.Warn("Cannot find user.");
                return;
            }
            info.Flags = PresenceInfo.PresenceInfoFlags.Banned;
            presenceInfo.UpdatePresenceInfo(info);
            m_log.Fatal("User blocked from logging in");
        }

        protected void UnBlockUser(string[] cmdparams)
        {
            UUID AgentID;
            PresenceInfo info;
            if (!UUID.TryParse(cmdparams[2], out AgentID))
            {
                UserAccount account = m_accountService.GetUserAccount(UUID.Zero, Util.CombineParams(cmdparams, 2));
                if (account == null)
                {
                    m_log.Warn("Cannot find user.");
                    return;
                }
                AgentID = account.PrincipalID;
            }
            info = GetInformation(AgentID);
            if (info == null)
            {
                m_log.Warn("Cannot find user.");
                return;
            }
            info.Flags = PresenceInfo.PresenceInfoFlags.Clean;
            presenceInfo.UpdatePresenceInfo(info);
            m_log.Fatal("User block removed");
        }

        protected void SetUserInfo(string[] cmdparams)
        {
            UUID AgentID;
            PresenceInfo info;
            int Num = 2;
            if (!UUID.TryParse(cmdparams[1], out AgentID))
            {
                UserAccount account = m_accountService.GetUserAccount(UUID.Zero, cmdparams[1], cmdparams[2]);
                if (account == null)
                {
                    m_log.Warn("Cannot find user.");
                    return;
                }
                AgentID = account.PrincipalID;
                Num += 1;
            }
            info = GetInformation(AgentID);
            if (info == null)
            {
                m_log.Warn("Cannot find user.");
                return;
            }
            try
            {
                info.Flags = (PresenceInfo.PresenceInfoFlags)Enum.Parse(typeof(PresenceInfo.PresenceInfoFlags), cmdparams[Num]);
            }
            catch
            {
                m_log.Warn("Please choose a valid flag: Clean, Suspected, Known, Banned");
                return;
            }
            m_log.Info("Set Flags for " + info.AgentID.ToString() + " to " + info.Flags.ToString());
            presenceInfo.UpdatePresenceInfo(info);
        }

        private void DisplayUserInfo(PresenceInfo info)
        {
            m_log.Info("User Info for " + info.AgentID);
            m_log.Info("   AgentID: " + info.AgentID);
            m_log.Info("   Flags: " + info.Flags);
            /*m_log.Info("   ID0: " + info.LastKnownID0);
            m_log.Info("   IP: " + info.LastKnownIP);
            m_log.Info("   Mac: " + info.LastKnownMac);
            m_log.Info("   Viewer: " + info.LastKnownViewer);
            m_log.Info("   Platform: " + info.Platform);*/
        }

        private bool CheckClient(UUID AgentID, out string message)
        {
            message = "";

            IAgentInfo data = DataManager.DataManager.RequestPlugin<IAgentConnector>().GetAgent(AgentID);
            if (data != null && ((data.Flags & IAgentFlags.PermBan) == IAgentFlags.PermBan || (data.Flags & IAgentFlags.TempBan) == IAgentFlags.TempBan))
            {
                message = "User is banned from the grid.";
                return false;
            }
            PresenceInfo info = GetInformation(AgentID);

            if (m_checkOnLogin)
                CheckForSimilarities(info);

            if (!CheckThreatLevel(info, out message))
                return false;

            if (!CheckViewer(info))
            {
                message = "This viewer has been blocked from connecting, please connect with a different viewer.";
                return false;
            }

            return true;
        }

        private bool CheckViewer(PresenceInfo info)
        {
            //Check for banned viewers
            foreach (string viewer in m_bannedViewers)
            {
                if (info.LastKnownViewer.Contains(viewer.StartsWith(" ") ? viewer.Remove(1) : viewer))
                    return false;
            }
            foreach (string mac in info.KnownMacs)
            {
                if (mac.Contains("000"))
                {
                    //Ban this asshole
                    return false;
                }
                //if (mac.Length != 32)
                //    return false; //Valid length!
            }
            foreach (string id0 in info.KnownID0s)
            {
                if (id0.Contains("000"))
                {
                    //Ban this asshole
                    return false;
                }
                //if (id0.Length != 32)
                //    return false; //Valid length!
            }
            
            return true;
        }

        private bool CheckThreatLevel(PresenceInfo info, out string message)
        {
            message = "";
            if ((info.Flags & PresenceInfo.PresenceInfoFlags.Banned) == PresenceInfo.PresenceInfoFlags.Banned)
            {
                message = "Banned agent.";
                return false;
            }
            if (GrieferAllowLevel == AllowLevel.AllowKnown)
                return true; //Allow all
            else if (GrieferAllowLevel == AllowLevel.AllowCleanOnly)
            { 
                //Allow people with only clean flag or suspected alt
                if ((info.Flags & PresenceInfo.PresenceInfoFlags.Clean) == PresenceInfo.PresenceInfoFlags.Clean)
                    return true;
                else
                {
                    message = "Not a Clean agent and have been denied access.";
                    return false;
                }
            }
            else if (GrieferAllowLevel == AllowLevel.AllowSuspected)
            {
                //Block all alts of knowns, and suspected alts of knowns
                if ((info.Flags & PresenceInfo.PresenceInfoFlags.Known) == PresenceInfo.PresenceInfoFlags.Known ||
                    (info.Flags & PresenceInfo.PresenceInfoFlags.SuspectedAltAccountOfKnown) == PresenceInfo.PresenceInfoFlags.SuspectedAltAccountOfKnown || 
                    (info.Flags & PresenceInfo.PresenceInfoFlags.KnownAltAccountOfKnown) == PresenceInfo.PresenceInfoFlags.KnownAltAccountOfKnown)
                {
                    message = "Not a Clean agent and have been denied access.";
                    return false;
                }
                else
                    return true;
            }

            return true;
        }

        #endregion

        #region Public members

        public bool CheckUser(UUID AgentID, string ip, string version, string platform, string mac, string id0, out string message)
        {
            message = "";
            if (!m_enabled)
                return true;

            PresenceInfo oldInfo = GetInformation(AgentID);
            oldInfo = UpdatePresenceInfo(AgentID, oldInfo, ip, version, platform, mac, id0);
            if (m_debug)
                DisplayUserInfo(oldInfo);

            return CheckClient(AgentID, out message);
        }

        public void SetUserLevel(UUID AgentID, PresenceInfo.PresenceInfoFlags presenceInfoFlags)
        {
            if (!m_enabled)
                return;
            //Get
            PresenceInfo info = GetInformation(AgentID);
            //Set the flags
            info.Flags = presenceInfoFlags;
            //Save
            presenceInfo.UpdatePresenceInfo(info);
        }

        #endregion
    }

    #endregion
}
