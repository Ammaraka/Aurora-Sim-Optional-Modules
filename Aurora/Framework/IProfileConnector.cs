﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;

namespace Aurora.Framework
{
	public interface IProfileConnector
	{
		Classified FindClassified(string classifiedID);
		ProfilePickInfo FindPick(string pickID);
        IUserProfileInfo GetUserProfile(UUID agentID);
        void UpdateUserNotes(UUID agentID, UUID targetAgentID, string notes, IUserProfileInfo UPI);
		bool UpdateUserProfile(IUserProfileInfo Profile);
        void UpdateUserInterests(IUserProfileInfo Profile);
		void CreateNewProfile(UUID UUID);
		void RemoveFromCache(UUID ID);
        void AddClassified(Classified classified);
        void DeleteClassified(UUID ID, UUID agentID);
        void AddPick(ProfilePickInfo pick);
        void UpdatePick(ProfilePickInfo pick);
        void DeletePick(UUID ID, UUID agentID);
    }
}
