﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.GameObjects.Conditions;
using Intersect.GameObjects.Crafting;
using Intersect.GameObjects.Events;
using Intersect.Server.Classes.Localization;
using Intersect.Server.Classes.Core;
using Intersect.Server.Classes.Database;
using Intersect.Server.Classes.Database.PlayerData.Characters;
using Intersect.Server.Classes.General;

using Intersect.Server.Classes.Maps;
using Intersect.Server.Classes.Networking;
using Intersect.Server.Classes.Spells;

namespace Intersect.Server.Classes.Entities
{
    using LegacyDatabase = Intersect.Server.Classes.Core.LegacyDatabase;

    public class EventInstance
    {
        public Guid Id;

        public EventBase BaseEvent;

        public Stack<CommandInstance> CallStack = new Stack<CommandInstance>();
        public int CurrentX;
        public int CurrentY;
        public EventPageInstance[] GlobalPageInstance;
        public bool HoldingPlayer;
        public bool IsGlobal;
        public Guid MapId;
        public Client MyClient;
        public Player MyPlayer;
        public bool NpcDeathTriggerd;
        public int PageIndex;
        public EventPageInstance PageInstance;

        //Special conditions
        public bool PlayerHasDied;
        public int SpawnX;
        public int SpawnY;
        public long WaitTimer;

        public EventInstance(Guid instanceId, Guid map, Client client, EventBase baseEvent)
        {
            Id = instanceId;
            MyClient = client;
            MapId = map;
            MyPlayer = client.Entity;
            SelfSwitch = new bool[4];
            BaseEvent = baseEvent;
            MapId = map;
            CurrentX = baseEvent.SpawnX;
            CurrentY = baseEvent.SpawnY;
        }

        public EventInstance(Guid instanceId, EventBase baseEvent,Guid map) //Global constructor
        {
            Id = instanceId;
            IsGlobal = true;
            MapId = map;
            BaseEvent = baseEvent;
            SelfSwitch = new bool[4];
            GlobalPageInstance = new EventPageInstance[BaseEvent.Pages.Count];
            CurrentX = baseEvent.SpawnX;
            CurrentY = baseEvent.SpawnY;
            for (int i = 0; i < BaseEvent.Pages.Count; i++)
            {
                GlobalPageInstance[i] = new EventPageInstance(BaseEvent, BaseEvent.Pages[i], MapId, this, null);
            }
        }

        public bool[] SelfSwitch { get; set; }

        public void Update(long timeMs)
        {
            var sendLeave = false;
            if (PageInstance != null)
            {
                //Check for despawn
                if (PageInstance.ShouldDespawn())
                {
                    CurrentX = PageInstance.X;
                    CurrentY = PageInstance.Y;
                    PageInstance = null;
                    PlayerHasDied = false;
                    if (HoldingPlayer)
                    {
                        PacketSender.SendReleasePlayer(MyClient, Id);
                        HoldingPlayer = false;
                    }
                    sendLeave = true;
                }
                else
                {
                    if (!IsGlobal)
                        PageInstance.Update(CallStack.Count > 0,
                            timeMs); //Process movement and stuff that is client specific
                    if (CallStack.Count > 0)
                    {
                        if (CallStack.Peek().WaitingForResponse == CommandInstance.EventResponse.Shop &&
                            MyPlayer.InShop == null)
                            CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.None;
                        if (CallStack.Peek().WaitingForResponse == CommandInstance.EventResponse.Crafting &&
                            MyPlayer.CraftingTableId == Guid.Empty)
                            CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.None;
                        if (CallStack.Peek().WaitingForResponse == CommandInstance.EventResponse.Bank &&
                            MyPlayer.InBank == false)
                            CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.None;
                        if (CallStack.Peek().WaitingForResponse == CommandInstance.EventResponse.Quest &&
                            !MyPlayer.QuestOffers.Contains(CallStack.Peek().ResponseId))
                            CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.None;
                        while (CallStack.Peek().WaitingForResponse == CommandInstance.EventResponse.None)
                        {
                            if (CallStack.Peek().WaitingForRoute != Guid.Empty)
                            {
                                if (CallStack.Peek().WaitingForRoute == MyPlayer.Id)
                                {
                                    if (MyPlayer.MoveRoute == null ||
                                        (MyPlayer.MoveRoute.Complete &&
                                         MyPlayer.MoveTimer < Globals.System.GetTimeMs()))
                                    {
                                        CallStack.Peek().WaitingForRoute = Guid.Empty;
                                        CallStack.Peek().WaitingForRouteMap = Guid.Empty;
                                    }
                                }
                                else
                                {
                                    //Check if the exist exists && if the move route is completed.
                                    foreach (var evt in MyPlayer.EventLookup.Values)
                                    {
                                        if (evt.MapId == CallStack.Peek().WaitingForRouteMap && evt.BaseEvent.Id == CallStack.Peek().WaitingForRoute)
                                        {
                                            if (evt.PageInstance == null) break;
                                            if (!evt.PageInstance.MoveRoute.Complete) break;
                                            CallStack.Peek().WaitingForRoute = Guid.Empty;
                                            CallStack.Peek().WaitingForRouteMap = Guid.Empty;
                                            break;
                                        }
                                    }
                                }
                                if (CallStack.Peek().WaitingForRoute != Guid.Empty) break;
                            }
                            else
                            {
                                if (CallStack.Peek().CommandIndex >=
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                        .Commands.Count)
                                {
                                    CallStack.Pop();
                                }
                                else
                                {
                                    if (WaitTimer < Globals.System.GetTimeMs())
                                    {
                                        ProcessCommand(
                                            CallStack.Peek().Page.CommandLists[
                                                    CallStack.Peek().ListIndex]
                                                .Commands[CallStack.Peek().CommandIndex]);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                if (CallStack.Count == 0)
                                {
                                    PlayerHasDied = false;
                                    NpcDeathTriggerd = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (PageInstance.Trigger == 2)
                        {
                            var newStack = new CommandInstance(PageInstance.MyPage) {CommandIndex = 0, ListIndex = 0};
                            CallStack.Push(newStack);
                        }
                    }
                }
            }

            if (PageInstance == null)
            {
                //Try to Spawn a PageInstance.. if we can
                for (int i = BaseEvent.Pages.Count - 1; i >= 0; i--)
                {
                    if (CanSpawnPage(i, BaseEvent))
                    {
                        if (IsGlobal)
                        {
                            if (MapInstance.Lookup.Get<MapInstance>(MapId).GetGlobalEventInstance(BaseEvent) != null)
                            {
                                PageInstance = new EventPageInstance(BaseEvent, BaseEvent.Pages[i],BaseEvent.Id,MapId, this, MyClient, MapInstance.Lookup.Get<MapInstance>(MapId).GetGlobalEventInstance(BaseEvent).GlobalPageInstance[i]);
                                sendLeave = false;
                                PageIndex = i;
                            }
                        }
                        else
                        {
                            PageInstance = new EventPageInstance(BaseEvent, BaseEvent.Pages[i], MapId, this, MyClient);
                            sendLeave = false;
                            PageIndex = i;
                        }
                        break;
                    }
                }

                if (sendLeave)
                {
                    if (IsGlobal)
                    {
                        PacketSender.SendEntityLeaveTo(MyClient, Id, (int) EntityTypes.Event, MapId);
                    }
                    else
                    {
                        PacketSender.SendEntityLeaveTo(MyClient, Id, (int) EntityTypes.Event, MapId);
                    }
                }
            }
        }

        public bool CanSpawnPage(int pageIndex, EventBase eventStruct)
        {
            return MeetsConditionLists(eventStruct.Pages[pageIndex].ConditionLists, MyPlayer, this);
        }

        public static bool MeetsConditionLists(ConditionLists lists, Player myPlayer, EventInstance eventInstance,
            bool singleList = true, QuestBase questBase = null)
        {
            if (myPlayer == null) return false;
            //If no condition lists then this passes
            if (lists.Lists.Count == 0)
                return true;

            for (int i = 0; i < lists.Lists.Count; i++)
            {
                if (MeetsConditionList(lists.Lists[i], myPlayer, eventInstance,questBase))
                    //Checks to see if all conditions in this list are met
                {
                    //If all conditions are met.. and we only need a single list to pass then return true
                    if (singleList)
                        return true;

                    continue;
                }

                //If not.. and we need all lists to pass then return false
                if (!singleList)
                    return false;
            }
            //There were condition lists. If single list was true then we failed every single list and should return false.
            //If single list was false (meaning we needed to pass all lists) then we've made it.. return true.
            return !singleList;
        }

        public static bool MeetsConditionList(ConditionList list, Player myPlayer, EventInstance eventInstance, QuestBase questBase)
        {
            for (int i = 0; i < list.Conditions.Count; i++)
            {
                if (!MeetsCondition(list.Conditions[i], myPlayer, eventInstance, questBase)) return false;
            }
            return true;
        }

        public static bool MeetsCondition(EventCommand conditionCommand, Player myPlayer, EventInstance eventInstance, QuestBase questBase)
        {
            //For instance use PageInstance
            switch (conditionCommand.Ints[0])
            {
                case 0: //Player Switch
                    var switchVal = myPlayer.GetSwitchValue(conditionCommand.Ints[1]);
                    if (switchVal == Convert.ToBoolean(conditionCommand.Ints[2]))
                    {
                        return true;
                    }
                    break;
                case 1: //Player Variable
                    var varVal = myPlayer.GetVariableValue(conditionCommand.Ints[1]);
                    switch (conditionCommand.Ints[2]) //Comparator
                    {
                        case 0: //Equal to
                            if (varVal == conditionCommand.Ints[3])
                                return true;
                            break;
                        case 1: //Greater than or equal to
                            if (varVal >= conditionCommand.Ints[3])
                                return true;
                            break;
                        case 2: //Less than or equal to
                            if (varVal <= conditionCommand.Ints[3])
                                return true;
                            break;
                        case 3: //Greater than
                            if (varVal > conditionCommand.Ints[3])
                                return true;
                            break;
                        case 4: //Less than
                            if (varVal < conditionCommand.Ints[3])
                                return true;
                            break;
                        case 5: //Does not equal
                            if (varVal != conditionCommand.Ints[3])
                                return true;
                            break;
                    }
                    break;
                case 2: //Global Switch
                    var servSwitch = false;
                    if (ServerSwitchBase.Lookup.Get<ServerSwitchBase>(conditionCommand.Guids[1]) != null)
                        servSwitch = ServerSwitchBase.Lookup.Get<ServerSwitchBase>(conditionCommand.Guids[1]).Value;
                    if (servSwitch == Convert.ToBoolean(conditionCommand.Ints[2]))
                        return true;
                    break;
                case 3: //Global Variable
                    var servVar = 0;
                    if (ServerVariableBase.Lookup.Get<ServerVariableBase>(conditionCommand.Guids[1]) != null)
                        servVar = ServerVariableBase.Lookup.Get<ServerVariableBase>(conditionCommand.Guids[1]).Value;
                    switch (conditionCommand.Ints[2]) //Comparator
                    {
                        case 0: //Equal to
                            if (servVar == conditionCommand.Ints[3])
                                return true;
                            break;
                        case 1: //Greater than or equal to
                            if (servVar >= conditionCommand.Ints[3])
                                return true;
                            break;
                        case 2: //Less than or equal to
                            if (servVar <= conditionCommand.Ints[3])
                                return true;
                            break;
                        case 3: //Greater than
                            if (servVar > conditionCommand.Ints[3])
                                return true;
                            break;
                        case 4: //Less than
                            if (servVar < conditionCommand.Ints[3])
                                return true;
                            break;
                        case 5: //Does not equal
                            if (servVar != conditionCommand.Ints[3])
                                return true;
                            break;
                    }
                    break;
                case 4: //Has Item
                    if (myPlayer.FindItem(conditionCommand.Guids[1], conditionCommand.Ints[2]) > -1)
                    {
                        return true;
                    }
                    break;
                case 5: //Class Is
                    if (myPlayer.ClassId == conditionCommand.Guids[1])
                    {
                        return true;
                    }
                    break;
                case 6: //Knows spell
                    if (myPlayer.KnowsSpell(conditionCommand.Guids[1]))
                    {
                        return true;
                    }
                    break;
                case 7: //Level or Stat is
                    var lvlStat = 0;
                    if (conditionCommand.Ints[3] == 0)
                    {
                        lvlStat = myPlayer.Level;
                    }
                    else
                    {
                        lvlStat = myPlayer.Stat[conditionCommand.Ints[3] - 1].Stat;
                    }
                    switch (conditionCommand.Ints[1])
                    {
                        case 0:
                            if (lvlStat == conditionCommand.Ints[2]) return true;
                            break;
                        case 1:
                            if (lvlStat >= conditionCommand.Ints[2]) return true;
                            break;
                        case 2:
                            if (lvlStat <= conditionCommand.Ints[2]) return true;
                            break;
                        case 3:
                            if (lvlStat > conditionCommand.Ints[2]) return true;
                            break;
                        case 4:
                            if (lvlStat < conditionCommand.Ints[2]) return true;
                            break;
                        case 5:
                            if (lvlStat != conditionCommand.Ints[2]) return true;
                            break;
                    }
                    break;
                case 8: //Self Switch
                    if (eventInstance != null)
                    {
                        if (eventInstance.IsGlobal)
                        {
                            var evts = MapInstance.Lookup.Get<MapInstance>(eventInstance.MapId).GlobalEventInstances
                                .Values.ToList();
                            for (int i = 0; i < evts.Count; i++)
                            {
                                if (evts[i] != null && evts[i].BaseEvent == eventInstance.BaseEvent)
                                {
                                    if (evts[i].SelfSwitch[conditionCommand.Ints[1]] ==
                                        Convert.ToBoolean(conditionCommand.Ints[2]))
                                        return true;
                                }
                            }
                        }
                        else
                        {
                            if (eventInstance.SelfSwitch[conditionCommand.Ints[1]] ==
                                Convert.ToBoolean(conditionCommand.Ints[2]))
                                return true;
                        }
                    }
                    return false;
                case 9: //Power Is
                    if (myPlayer.MyClient.Access > conditionCommand.Ints[1]) return true;
                    return false;
                case 10: //Time is between
                    if (conditionCommand.Ints[1] > -1 && conditionCommand.Ints[2] > -1 &&
                        conditionCommand.Ints[1] < 1440 / TimeBase.GetTimeBase().RangeInterval &&
                        conditionCommand.Ints[2] < 1440 / TimeBase.GetTimeBase().RangeInterval)
                    {
                        return (ServerTime.GetTimeRange() >= conditionCommand.Ints[1] &&
                                ServerTime.GetTimeRange() <= conditionCommand.Ints[2]);
                    }
                    else
                    {
                        return true;
                    }
                case 11: //Can Start Quest
                    var startQuest = QuestBase.Lookup.Get<QuestBase>(conditionCommand.Guids[1]);
                    if (startQuest == questBase)
                    {
                        //We cannot check and see if we meet quest requirements if we are already checking to see if we meet quest requirements :P
                        return true;
                    }
                    if (startQuest != null)
                    {
                        return myPlayer.CanStartQuest(startQuest);
                    }
                    break;
                case 12: //Quest In Progress
                    var questInProgress = QuestBase.Lookup.Get<QuestBase>(conditionCommand.Guids[1]);
                    if (questInProgress != null)
                    {
                        return myPlayer.QuestInProgress(questInProgress, (QuestProgress) conditionCommand.Ints[2],
                            conditionCommand.Ints[3]);
                    }
                    break;
                case 13: //Quest Completed
                    var questCompleted = QuestBase.Lookup.Get<QuestBase>(conditionCommand.Guids[1]);
                    if (questCompleted != null)
                    {
                        return myPlayer.QuestCompleted(questCompleted);
                    }
                    break;
                case 14: //Player death
                    if (eventInstance != null)
                    {
                        return eventInstance.PlayerHasDied;
                    }
                    return false;
                case 15: //no NPCs on the map (used for boss fights)
                    if (eventInstance != null)
                    {
                        if (eventInstance.NpcDeathTriggerd == true) return false; //Only call it once
                        MapInstance m = MapInstance.Lookup.Get<MapInstance>(eventInstance.MapId);
                        for (int i = 0; i < m.Spawns.Count; i++)
                        {
                            if (m.NpcSpawnInstances.ContainsKey(m.Spawns[i]))
                            {
                                if (m.NpcSpawnInstances[m.Spawns[i]].Entity.Dead == false)
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                    break;
                case 16: //Gender is
                    return myPlayer.Gender == conditionCommand.Ints[1];
            }
            return false;
        }

        private string ParseEventText(string input)
        {
            if (MyClient != null && MyClient.Entity != null)
            {
                input = input.Replace(Strings.Events.playernamecommand, MyClient.Entity.Name);
                input = input.Replace(Strings.Events.eventnamecommand, PageInstance.Name);
                input = input.Replace(Strings.Events.commandparameter, PageInstance.Param);
                if (input.Contains(Strings.Events.onlinelistcommand) ||
                    input.Contains(Strings.Events.onlinecountcommand))
                {
                    var onlineList = Globals.OnlineList;
                    input = input.Replace(Strings.Events.onlinecountcommand, onlineList.Count.ToString());
                    var sb = new StringBuilder();
                    for (int i = 0; i < onlineList.Count; i++)
                    {
                        sb.Append(onlineList[i].Name + (i != onlineList.Count - 1 ? ", " : ""));
                    }
                    input = input.Replace(Strings.Events.onlinelistcommand, sb.ToString());
                }

                //Time Stuff
                input = input.Replace(Strings.Events.timehour, ServerTime.GetTime().ToString("%h"));
                input = input.Replace(Strings.Events.militaryhour, ServerTime.GetTime().ToString("HH"));
                input = input.Replace(Strings.Events.timeminute, ServerTime.GetTime().ToString("mm"));
                input = input.Replace(Strings.Events.timesecond, ServerTime.GetTime().ToString("ss"));
                if (ServerTime.GetTime().Hour >= 12)
                {
                    input = input.Replace(Strings.Events.timeperiod, Strings.Events.periodevening);
                }
                else
                {
                    input = input.Replace(Strings.Events.timeperiod, Strings.Events.periodmorning);
                }

                //Have to accept a numeric parameter after each of the following (player switch/var and server switch/var)
                MatchCollection matches = Regex.Matches(input, Regex.Escape(Strings.Events.playervar) + " ([0-9]+)");
                foreach (Match m in matches)
                {
                    if (m.Success)
                    {
                        int id = Convert.ToInt32(m.Groups[1].Value);
                        input = input.Replace(Strings.Events.playervar + " " + m.Groups[1].Value,
                            MyPlayer.GetVariableValue(id).ToString());
                    }
                }
                matches = Regex.Matches(input, Regex.Escape(Strings.Events.playerswitch) + " ([0-9]+)");
                foreach (Match m in matches)
                {
                    if (m.Success)
                    {
                        int id = Convert.ToInt32(m.Groups[1].Value);
                        input = input.Replace(Strings.Events.playerswitch + " " + m.Groups[1].Value,
                            MyPlayer.GetSwitchValue(id).ToString());
                    }
                }
                matches = Regex.Matches(input, Regex.Escape(Strings.Events.globalvar) + " ([0-9]+)");
                foreach (Match m in matches)
                {
                    if (m.Success)
                    {
                        //int id = Convert.ToInt32(m.Groups[1].Value);
                        //var globalvar = ServerVariableBase.Lookup.Get<ServerVariableBase>(id);
                        //if (globalvar != null)
                        //{
                        //    input = input.Replace(Strings.Events.globalvar + " " + m.Groups[1].Value,
                        //        globalvar.Value.ToString());
                        //}
                        //else
                        //{
                        //    input = input.Replace(Strings.Events.globalvar + " " + m.Groups[1].Value,
                        //        0.ToString());
                        //}
                    }
                }
                matches = Regex.Matches(input, Regex.Escape(Strings.Events.globalswitch) + " ([0-9]+)");
                foreach (Match m in matches)
                {
                    //if (m.Success)
                    //{
                    //    int id = Convert.ToInt32(m.Groups[1].Value);
                    //    var globalswitch = ServerSwitchBase.Lookup.Get<ServerSwitchBase>(id);
                    //    if (globalswitch != null)
                    //    {
                    //        input = input.Replace(Strings.Events.globalswitch + " " + m.Groups[1].Value,
                    //            globalswitch.Value.ToString());
                    //    }
                    //    else
                    //    {
                    //        input = input.Replace(Strings.Events.globalswitch + " " + m.Groups[1].Value,
                    //            false.ToString());
                    //    }
                    //}
                }
            }
            return input;
        }

        private void ProcessCommand(EventCommand command)
        {
            bool success = false;
            TileHelper tile;
            int spawnCondition, tileX = 0, tileY = 0, direction = (int) Directions.Up;
            Guid npcId, animId, mapId;
            EntityInstance targetEntity = null;
            CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.None;
            CallStack.Peek().ResponseId = Guid.Empty;
            switch (command.Type)
            {
                case EventCommandType.ShowText:
                    PacketSender.SendEventDialog(MyClient, ParseEventText(command.Strs[0]), command.Strs[1], BaseEvent.Id);
                    CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.Dialogue;
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ShowOptions:
                    PacketSender.SendEventDialog(MyClient, ParseEventText(command.Strs[0]),
                        ParseEventText(command.Strs[1]), ParseEventText(command.Strs[2]),
                        ParseEventText(command.Strs[3]), ParseEventText(command.Strs[4]), command.Strs[5], BaseEvent.Id);
                    CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.Dialogue;
                    break;
                case EventCommandType.AddChatboxText:
                    switch (command.Ints[0])
                    {
                        case 0: //Player
                            PacketSender.SendPlayerMsg(MyClient, ParseEventText(command.Strs[0]),
                                Color.FromName(command.Strs[1], Strings.Colors.presets));
                            break;
                        case 1: //Local
                            PacketSender.SendProximityMsg(ParseEventText(command.Strs[0]), MyClient.Entity.MapId,
                                Color.FromName(command.Strs[1], Strings.Colors.presets));
                            break;
                        case 2: //Global
                            PacketSender.SendGlobalMsg(ParseEventText(command.Strs[0]),
                                Color.FromName(command.Strs[1], Strings.Colors.presets));
                            break;
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SetSwitch:
                    if (command.Ints[0] == (int) SwitchVariableTypes.PlayerSwitch)
                    {
                        MyPlayer.SetSwitchValue(command.Ints[1], Convert.ToBoolean(command.Ints[2]));
                    }
                    else if (command.Ints[0] == (int) SwitchVariableTypes.ServerSwitch)
                    {
                        var serverSwitch = ServerSwitchBase.Lookup.Get<ServerSwitchBase>(command.Guids[1]);
                        if (serverSwitch != null)
                        {
                            serverSwitch.Value = Convert.ToBoolean(command.Ints[2]);
                            LegacyDatabase.SaveGameDatabase();
                        }
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SetVariable:
                    if (command.Ints[0] == (int) SwitchVariableTypes.PlayerVariable)
                    {
                        switch (command.Ints[2])
                        {
                            case 0: //Set
                                MyPlayer.SetVariableValue(command.Ints[1], command.Ints[3]);
                                break;
                            case 1: //Add
                                MyPlayer.SetVariableValue(command.Ints[1], MyPlayer.GetVariableValue(command.Ints[1]) + command.Ints[3]);
                                break;
                            case 2: //Subtract
                                MyPlayer.SetVariableValue(command.Ints[1], MyPlayer.GetVariableValue(command.Ints[1]) - command.Ints[3]);
                                break;
                            case 3: //Random
                                MyPlayer.SetVariableValue(command.Ints[1], Globals.Rand.Next(command.Ints[3], command.Ints[4] + 1));
                                break;
                        }
                    }
                    else if (command.Ints[0] == (int) SwitchVariableTypes.ServerVariable)
                    {
                        var serverVarible = ServerVariableBase.Lookup.Get<ServerVariableBase>(command.Guids[1]);
                        if (serverVarible != null)
                        {
                            switch (command.Ints[2])
                            {
                                case 0: //Set
                                    serverVarible.Value = command.Ints[3];
                                    break;
                                case 1: //Add
                                    serverVarible.Value += command.Ints[3];
                                    break;
                                case 2: //Subtract
                                    serverVarible.Value -= command.Ints[3];
                                    break;
                                case 3: //Random
                                    serverVarible.Value = Globals.Rand.Next(command.Ints[3], command.Ints[4] + 1);
                                    break;
                            }
                        }
                        LegacyDatabase.SaveGameDatabase();
                    }

                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SetSelfSwitch:
                    if (IsGlobal)
                    {
                        var evts = MapInstance.Lookup.Get<MapInstance>(MapId).GlobalEventInstances.Values.ToList();
                        for (int i = 0; i < evts.Count; i++)
                        {
                            if (evts[i] != null && evts[i].BaseEvent == BaseEvent)
                            {
                                evts[i].SelfSwitch[command.Ints[0]] = Convert.ToBoolean(command.Ints[1]);
                            }
                        }
                    }
                    else
                    {
                        SelfSwitch[command.Ints[0]] = Convert.ToBoolean(command.Ints[1]);
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ConditionalBranch:
                    if (MeetsCondition(command, MyPlayer, this,null))
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[4]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    else
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[5]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    break;
                case EventCommandType.ExitEventProcess:
                    CallStack.Clear();
                    return;
                case EventCommandType.Label:
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.GoToLabel:
                    //Recursively search through commands for the label, and create a brand new call stack based on where that label is located.
                    Stack<CommandInstance> newCallStack = LoadLabelCallstack(command.Strs[0], CallStack.Peek().Page);
                    if (newCallStack != null)
                    {
                        CallStack = newCallStack;
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.StartCommonEvent:
                    CallStack.Peek().CommandIndex++;
                    var commonEvent = EventBase.Lookup.Get<EventBase>(command.Guids[0]);
                    if (commonEvent != null)
                    {
                        for (int i = 0; i < commonEvent.Pages.Count; i++)
                        {
                            if (CanSpawnPage(i, commonEvent))
                            {
                                var commonEventStack =
                                    new CommandInstance(commonEvent.Pages[i])
                                    {
                                        CommandIndex = 0,
                                        ListIndex = 0,
                                    };

                                CallStack.Push(commonEventStack);
                            }
                        }
                    }

                    break;
                case EventCommandType.RestoreHp:
                    MyPlayer.RestoreVital(Vitals.Health);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.RestoreMp:
                    MyPlayer.RestoreVital(Vitals.Mana);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.LevelUp:
                    MyPlayer.LevelUp();
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.GiveExperience:
                    // TODO: Long exp
                    MyPlayer.GiveExperience(command.Ints[0]);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ChangeLevel:
                    MyPlayer.SetLevel(command.Ints[0], true);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ChangeSpells:
                    //0 is add, 1 is remove
                    success = false;
                    if (command.Ints[0] == 0) //Try to add a spell
                    {
                        success = MyPlayer.TryTeachSpell(new Spell(command.Guids[1]));
                    }
                    else
                    {
                        if (MyPlayer.FindSpell(command.Guids[1]) > -1)
                        {
                            MyPlayer.ForgetSpell(MyPlayer.FindSpell(command.Guids[1]));
                            success = true;
                        }
                    }
                    if (success)
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[4]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    else
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[5]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    break;
                case EventCommandType.ChangeItems:
                    //0 is give, 1 is take
                    success = false;
                    if (command.Ints[0] == 0) //Try to give item
                    {
                        success = MyPlayer.TryGiveItem(new Item(command.Guids[1], command.Ints[2]));
                    }
                    else
                    {
                        success = MyPlayer.TakeItemsById(command.Guids[1], command.Ints[2]);
                    }
                    if (success)
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[4]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    else
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[5]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    break;
                case EventCommandType.ChangeSprite:
                    MyPlayer.Sprite = command.Strs[0];
                    PacketSender.SendEntityDataToProximity(MyPlayer);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ChangeFace:
                    MyPlayer.Face = command.Strs[0];
                    PacketSender.SendEntityDataToProximity(MyPlayer);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ChangeGender:
                    MyPlayer.Gender = command.Ints[0];
                    PacketSender.SendEntityDataToProximity(MyPlayer);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SetAccess:
                    MyPlayer.MyClient.Access = command.Ints[0];
                    PacketSender.SendEntityDataToProximity(MyPlayer);
                    PacketSender.SendPlayerMsg(MyPlayer.MyClient, Strings.Player.powerchanged, Color.Red);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.WarpPlayer:
                    MyPlayer.Warp(
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Guids[0],
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Ints[1],
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Ints[2],
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Ints[3] == 0 ? MyPlayer.Dir : CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Ints[3] -1);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SetMoveRoute:
                    if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Route.Target == Guid.Empty)
                    {
                        MyClient.Entity.MoveRoute = new EventMoveRoute();
                        MyClient.Entity.MoveRouteSetter = PageInstance;
                        MyClient.Entity.MoveRoute.CopyFrom(
                            CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                .Commands[CallStack.Peek().CommandIndex].Route);
                        PacketSender.SendMoveRouteToggle(MyClient, true);
                    }
                    else
                    {
                        foreach (var evt in MyPlayer.EventLookup.Values)
                        {
                            if (evt.BaseEvent.Id == CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Route.Target)
                            {
                                if (evt.PageInstance != null)
                                {
                                    evt.PageInstance.MoveRoute.CopyFrom(CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Route);
                                    evt.PageInstance.MovementType = 2;
                                    if (evt.PageInstance.GlobalClone != null)
                                        evt.PageInstance.GlobalClone.MovementType = 2;
                                }
                            }
                        }
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.WaitForRouteCompletion:
                    if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Ints[0] == -1)
                    {
                        CallStack.Peek().WaitingForRoute = MyPlayer.Id;
                        CallStack.Peek().WaitingForRouteMap = MyPlayer.MapId;
                    }
                    else
                    {
                        foreach (var evt in MyPlayer.EventLookup.Values)
                        {
                            if (evt.BaseEvent.Id == CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[0])
                            {
                                CallStack.Peek().WaitingForRoute = evt.BaseEvent.Id;
                                CallStack.Peek().WaitingForRouteMap = evt.MapId;
                                break;
                            }
                        }
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SpawnNpc:
                    npcId = CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[ CallStack.Peek().CommandIndex].Guids[0];
                    spawnCondition = CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[ CallStack.Peek().CommandIndex].Ints[1];
                    mapId = Guid.Empty;
                    tileX = 0;
                    tileY = 0;
                    direction = (int) Directions.Up;
                    targetEntity = null;
                    switch (spawnCondition)
                    {
                        case 0: //Tile Spawn
                            mapId =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Guids[2];
                            tileX =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Ints[3];
                            tileY =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Ints[4];
                            direction =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Ints[5];
                            break;
                        case 1: //Entity Spawn
                            if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[
                                        CallStack.Peek().CommandIndex].Ints[2] == -1)
                            {
                                targetEntity = MyPlayer;
                            }
                            else
                            {
                                foreach (var evt in MyPlayer.EventLookup.Values)
                                {
                                    if (evt.MapId != this.MapId) continue;
                                    if (evt.BaseEvent.Id == CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[2])
                                    {
                                        targetEntity = evt.PageInstance;
                                        break;
                                    }
                                }
                            }
                            if (targetEntity != null)
                            {
                                int xDiff =
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                        CallStack.Peek().CommandIndex].Ints[3];
                                int yDiff =
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                        CallStack.Peek().CommandIndex].Ints[4];
                                if (
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                        .Commands[CallStack.Peek().CommandIndex].Ints[5] == 1)
                                {
                                    int tmp = 0;
                                    switch (targetEntity.Dir)
                                    {
                                        case (int) Directions.Down:
                                            yDiff *= -1;
                                            xDiff *= -1;
                                            break;
                                        case (int) Directions.Left:
                                            tmp = yDiff;
                                            yDiff = xDiff;
                                            xDiff = tmp;
                                            break;
                                        case (int) Directions.Right:
                                            tmp = yDiff;
                                            yDiff = xDiff;
                                            xDiff = -tmp;
                                            break;
                                    }
                                    direction = targetEntity.Dir;
                                }
                                mapId = targetEntity.MapId;
                                tileX = targetEntity.X + xDiff;
                                tileY = targetEntity.Y + yDiff;
                            }
                            break;
                    }
                    tile = new TileHelper(mapId, tileX, tileY);
                    if (tile.TryFix())
                    {
                        var npc = MapInstance.Lookup.Get<MapInstance>(mapId)
                            .SpawnNpc(tileX, tileY, direction, npcId, true);
                        MyPlayer.SpawnedNpcs.Add((Npc) npc);
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.DespawnNpc:
                    var entities = MyPlayer.SpawnedNpcs.ToArray();
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (entities[i] != null && entities[i].GetType() == typeof(Npc))
                        {
                            if (((Npc) entities[i]).Despawnable == true)
                            {
                                ((Npc) entities[i]).Die(100);
                            }
                        }
                    }
                    MyPlayer.SpawnedNpcs.Clear();
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.PlayAnimation:
                    //Playing an animations requires a target type/target or just a tile.
                    //We need an animation number and whether or not it should rotate (and the direction I guess)
                    animId =CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[0];
                    spawnCondition =  CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Ints[1];
                    mapId = Guid.Empty;
                    tileX = 0;
                    tileY = 0;
                    direction = (int) Directions.Up;
                    targetEntity = null;
                    switch (spawnCondition)
                    {
                        case 0: //Tile Spawn
                            mapId =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Guids[2];
                            tileX =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Ints[3];
                            tileY =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Ints[4];
                            direction =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                    CallStack.Peek().CommandIndex].Ints[5];
                            break;
                        case 1: //Entity Spawn
                            if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[
                                        CallStack.Peek().CommandIndex].Ints[2] == -1)
                            {
                                targetEntity = MyPlayer;
                            }
                            else
                            {
                                foreach (var evt in MyPlayer.EventLookup.Values)
                                {
                                    if (evt.MapId != this.MapId) continue;
                                    if (evt.BaseEvent.Id == CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[2])
                                    {
                                        targetEntity = evt.PageInstance;
                                        break;
                                    }
                                }
                            }
                            if (targetEntity != null)
                            {
                                int xDiff =
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                        CallStack.Peek().CommandIndex].Ints[3];
                                int yDiff =
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                                        CallStack.Peek().CommandIndex].Ints[4];
                                if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                        .Commands[CallStack.Peek().CommandIndex].Ints[5] == 2 ||
                                    CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                        .Commands[CallStack.Peek().CommandIndex].Ints[5] == 3)
                                    direction = targetEntity.Dir;
                                if (xDiff == 0 && yDiff == 0)
                                {
                                    if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                            .Commands[CallStack.Peek().CommandIndex].Ints[5] == 2 ||
                                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                            .Commands[CallStack.Peek().CommandIndex].Ints[5] == 3)
                                        direction = -1;
                                    //Send Animation on Npc
                                    if (targetEntity.GetType() == typeof(Player))
                                    {
                                        PacketSender.SendAnimationToProximity(animId, 1, targetEntity.Id,
                                            MyClient.Entity.MapId, 0, 0, direction);
                                        //Target Type 1 will be global entity
                                    }
                                    else
                                    {
                                        PacketSender.SendAnimationToProximity(animId, 2, targetEntity.Id,
                                            targetEntity.MapId, 0, 0, direction);
                                    }
                                    CallStack.Peek().CommandIndex++;
                                    return;
                                }
                                else
                                {
                                    //Determine the tile data
                                    if (CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                            .Commands[CallStack.Peek().CommandIndex].Ints[5] == 1 ||
                                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                            .Commands[CallStack.Peek().CommandIndex].Ints[5] == 3)
                                    {
                                        int tmp = 0;
                                        switch (targetEntity.Dir)
                                        {
                                            case (int) Directions.Down:
                                                yDiff *= -1;
                                                xDiff *= -1;
                                                break;
                                            case (int) Directions.Left:
                                                tmp = yDiff;
                                                yDiff = xDiff;
                                                xDiff = tmp;
                                                break;
                                            case (int) Directions.Right:
                                                tmp = yDiff;
                                                yDiff = xDiff;
                                                xDiff = -tmp;
                                                break;
                                        }
                                    }
                                    mapId = targetEntity.MapId;
                                    tileX = targetEntity.X + xDiff;
                                    tileY = targetEntity.Y + yDiff;
                                }
                            }
                            break;
                    }
                    tile = new TileHelper(mapId, tileX, tileY);
                    if (tile.TryFix())
                    {
                        PacketSender.SendAnimationToProximity(animId, -1, Guid.Empty, tile.GetMapId(), tile.GetX(), tile.GetY(),direction);
                    }
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.HoldPlayer:
                    HoldingPlayer = true;
                    PacketSender.SendHoldPlayer(MyClient, Id,BaseEvent.MapId);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ReleasePlayer:
                    HoldingPlayer = false;
                    PacketSender.SendReleasePlayer(MyClient, Id);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.PlayBgm:
                    PacketSender.SendPlayMusic(MyClient,
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Strs[0]);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.FadeoutBgm:
                    PacketSender.SendFadeMusic(MyClient);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.PlaySound:
                    PacketSender.SendPlaySound(MyClient,
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[
                            CallStack.Peek().CommandIndex].Strs[0]);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.StopSounds:
                    PacketSender.SendStopSounds(MyClient);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.ShowPicture:
                    PacketSender.SendShowPicture(MyClient, command.Strs[0], command.Ints[0], command.Ints[1]);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.HidePicture:
                    PacketSender.SendHidePicture(MyClient);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.Wait:
                    WaitTimer = Globals.System.GetTimeMs() +
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[0];
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.OpenBank:
                    MyPlayer.OpenBank();
                    CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.Bank;
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.OpenShop:
                    MyPlayer.OpenShop(ShopBase.Get(CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                        .Commands[CallStack.Peek().CommandIndex].Guids[0]));
                    CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.Shop;
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.OpenCraftingTable:
                    MyPlayer.OpenCraftingTable(CraftingTableBase.Lookup.Get<CraftingTableBase>(CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[0]));
                    CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.Crafting;
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.SetClass:
                    if (ClassBase.Lookup.Get<ClassBase>(CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[0]) != null)
                    {
                        MyPlayer.ClassId = CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[0];
                    }
                    PacketSender.SendEntityDataToProximity(MyPlayer);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.StartQuest:
                    success = false;
                    var quest = QuestBase.Lookup.Get<QuestBase>(CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex].Commands[CallStack.Peek().CommandIndex].Guids[0]);
                    if (quest != null)
                    {
                        if (MyPlayer.CanStartQuest(quest))
                        {
                            var offer = Convert.ToBoolean(CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                .Commands[CallStack.Peek().CommandIndex].Ints[1]);
                            if (offer)
                            {
                                MyPlayer.OfferQuest(quest);
                                CallStack.Peek().WaitingForResponse = CommandInstance.EventResponse.Quest;
                                CallStack.Peek().ResponseId = quest.Id;
                                break;
                            }
                            else
                            {
                                MyPlayer.StartQuest(quest);
                                success = true;
                            }
                        }
                    }
                    if (success)
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[4]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    else
                    {
                        var tmpStack = new CommandInstance(CallStack.Peek().Page)
                        {
                            CommandIndex = 0,
                            ListIndex =
                                CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                                    .Commands[CallStack.Peek().CommandIndex].Ints[5]
                        };
                        CallStack.Peek().CommandIndex++;
                        CallStack.Push(tmpStack);
                    }
                    break;
                case EventCommandType.CompleteQuestTask:
                    MyPlayer.CompleteQuestTask(
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                            .Commands[CallStack.Peek().CommandIndex].Guids[0],
                        CallStack.Peek().Page.CommandLists[CallStack.Peek().ListIndex]
                            .Commands[CallStack.Peek().CommandIndex].Ints[1]);
                    CallStack.Peek().CommandIndex++;
                    break;
                case EventCommandType.EndQuest:
                    CallStack.Peek().CommandIndex++;
                    break;
            }
        }

        private Stack<CommandInstance> LoadLabelCallstack(string label, EventPage currentPage)
        {
            Stack<CommandInstance> newStack = new Stack<CommandInstance>();
            newStack.Push(new CommandInstance(currentPage) {CommandIndex = 0, ListIndex = 0}); //Start from the top
            if (FindLabelResursive(newStack, label))
            {
                return newStack;
            }
            return null;
        }

        private bool FindLabelResursive(Stack<CommandInstance> stack, string label)
        {
            if (stack.Peek().ListIndex < CallStack.Peek().Page.CommandLists.Count)
            {
                while (stack.Peek().CommandIndex <
                       CallStack.Peek().Page.CommandLists[stack.Peek().ListIndex].Commands.Count)
                {
                    EventCommand command =
                        CallStack.Peek().Page.CommandLists[stack.Peek().ListIndex].Commands[stack.Peek().CommandIndex];
                    switch (command.Type)
                    {
                        case EventCommandType.ShowOptions:
                            for (int i = 0; i < 4; i++)
                            {
                                var tmpStack = new CommandInstance(CallStack.Peek().Page)
                                {
                                    CommandIndex = 0,
                                    ListIndex =
                                        CallStack.Peek().Page.CommandLists[stack.Peek().ListIndex].Commands[
                                            stack.Peek().CommandIndex].Ints[i]
                                };
                                stack.Peek().CommandIndex++;
                                stack.Push(tmpStack);
                                if (FindLabelResursive(stack, label)) return true;
                                stack.Peek().CommandIndex--;
                            }
                            break;
                        case EventCommandType.ConditionalBranch:
                        case EventCommandType.ChangeSpells:
                        case EventCommandType.ChangeItems:
                            for (int i = 4; i <= 5; i++)
                            {
                                var tmpStack = new CommandInstance(CallStack.Peek().Page)
                                {
                                    CommandIndex = 0,
                                    ListIndex =
                                        CallStack.Peek().Page.CommandLists[stack.Peek().ListIndex].Commands[
                                            stack.Peek().CommandIndex].Ints[i]
                                };
                                stack.Peek().CommandIndex++;
                                stack.Push(tmpStack);
                                if (FindLabelResursive(stack, label)) return true;
                                stack.Peek().CommandIndex--;
                            }
                            break;
                        case EventCommandType.Label:
                            //See if we found the label!
                            if (
                                CallStack.Peek().Page.CommandLists[stack.Peek().ListIndex].Commands[
                                    stack.Peek().CommandIndex].Strs[0] == label)
                            {
                                return true;
                            }
                            break;
                    }
                    stack.Peek().CommandIndex++;
                }
                stack.Pop(); //We made it through a list
            }
            return false;
        }
    }

    public class CommandInstance
    {
        public enum EventResponse
        {
            None = 0,
            Dialogue,
            Shop,
            Bank,
            Crafting,
            Quest,
        }

        public int CommandIndex;
        public int ListIndex;
        public EventPage Page;
        public Guid ResponseId;
        public EventResponse WaitingForResponse = EventResponse.None;
        public Guid WaitingForRoute;
        public Guid WaitingForRouteMap;

        public CommandInstance(EventPage page)
        {
            Page = page;
        }
    }
}