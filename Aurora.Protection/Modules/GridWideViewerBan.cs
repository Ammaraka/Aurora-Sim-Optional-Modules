/*
 *  Copyright 2011 Matthew Beardmore
 *
 *  This file is part of Aurora.Addon.Protection.
 *  Aurora.Addon.Protection is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 *  Aurora.Addon.Protection is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 *  You should have received a copy of the GNU General Public License along with Aurora.Addon.Protection. If not, see http://www.gnu.org/licenses/.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using Aurora.Framework;
using Aurora.DataManager;
using Aurora.Simulation.Base;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Aurora.Protection
{
    public class GridWideViewerBan : IService
    {
        private List<string> m_bannedViewers = new List<string> ();
        private List<string> m_allowedViewers = new List<string> ();
        private bool m_enabled = true;
        private bool m_useIncludeList = false;
        private OSDMap m_map = null;
        private string m_viewerTagURL = "http://phoenixviewer.com/app/client_list.xml";
        private IRegistryCore m_registry;

        public void Initialize(IConfigSource source, IRegistryCore registry)
        {
            m_registry = registry;
            IConfig config = source.Configs["GrieferProtection"];
            if (config != null)
            {
                string bannedViewers = config.GetString ("ViewersToBan", "");
                m_bannedViewers = Util.ConvertToList(bannedViewers);
                string allowedViewers = config.GetString ("ViewersToAllow", "");
                m_allowedViewers = Util.ConvertToList(allowedViewers);
                m_viewerTagURL = config.GetString ("ViewerXMLURL", m_viewerTagURL);
                m_enabled = config.GetBoolean ("Enabled", true);
                m_useIncludeList = config.GetBoolean ("UseAllowListInsteadOfBanList", false);
                if (m_enabled)
                    registry.RequestModuleInterface<ISimulationBase> ().EventManager.RegisterEventHandler("SetAppearance", EventManager_OnGenericEvent);
            }
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
        }

        public void FinishedStartup()
        {
        }

        object EventManager_OnGenericEvent(string FunctionName, object parameters)
        {
            if (FunctionName == "SetAppearance")
            {
                object[] p = (object[])parameters;
                UUID avatarID = (UUID)p[0];
                AvatarData avatarData = (AvatarData)p[1];

                AvatarAppearance app = avatarData.ToAvatarAppearance (avatarID);
                CheckForBannedViewer (avatarID, app.Texture);
            }
            return null;
        }

        /// <summary>
        /// Check to see if the client has baked textures that belong to banned clients
        /// </summary>
        /// <param name="client"></param>
        /// <param name="textureEntry"></param>
        public void CheckForBannedViewer(UUID avatarID, Primitive.TextureEntry textureEntry)
        {
            try
            {
                //Read the website once!
                if (m_map == null)
                    m_map = OSDParser.Deserialize(Utilities.ReadExternalWebsite(m_viewerTagURL)) as OSDMap;
                if(m_map == null)
                    return;//Can't find it

                //This is the givaway texture!
                for (int i = 0; i < textureEntry.FaceTextures.Length; i++)
                {
                    if (textureEntry.FaceTextures[i] != null)
                    {
                        if (m_map.ContainsKey (textureEntry.FaceTextures[i].TextureID.ToString ()))
                        {
                            OSDMap viewerMap = (OSDMap)m_map[textureEntry.FaceTextures[i].TextureID.ToString ()];
                            //Check the names
                            if (IsViewerBanned (viewerMap["name"].ToString ()))
                            {
                                IGridWideMessageModule messageModule = m_registry.RequestModuleInterface<IGridWideMessageModule> ();
                                if (messageModule != null)
                                    messageModule.KickUser (avatarID, "You cannot use " + viewerMap["name"] + " in this grid.");
                                break;
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        public bool IsViewerBanned(string name)
        {
            if (m_useIncludeList)
            {
                if (!m_allowedViewers.Contains (name))
                    return true;
            }
            else
            {
                if (m_bannedViewers.Contains (name))
                    return true;
            }
            return false;
        }
    }
}
