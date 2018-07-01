﻿using System;
using System.Collections.Generic;
using Intersect.GameObjects.Conditions;
using Intersect.Utilities;
using Newtonsoft.Json;

namespace Intersect.GameObjects.Events
{
    public class EventPage
    {
        public enum CommonEventTriggers
        {
            None,
            JoinGame,
            LevelUp,
            OnRespawn,
            Command,
            Autorun,
        }

        public enum EventTriggers
        {
            ActionButton,
            OnTouch,
            Autorun,
            ProjectileHit,
        }

        public Guid AnimationId;
        public List<CommandList> CommandLists = new List<CommandList>();
        public ConditionLists ConditionLists = new ConditionLists();
        public string Desc = "";
        public int DirectionFix;
        public int DisablePreview = 1;
        public string FaceGraphic = "";
        public EventGraphic Graphic = new EventGraphic();
        public int HideName;
        public int InteractionFreeze;
        public int Layer;
        public int MovementFreq;
        public int MovementSpeed;
        public int MovementType;
        public EventMoveRoute MoveRoute = new EventMoveRoute();
        public int Passable;
        public int Trigger;
        public string TriggerCommand;
        public Guid TriggerVal;
        public int WalkingAnimation = 1;

        public EventPage()
        {
            MovementType = 0;
            MovementSpeed = 2;
            MovementFreq = 2;
            Passable = 0;
            Layer = 1;
            Trigger = 0;
            HideName = 0;
            CommandLists.Add(new CommandList());
        }

        [JsonConstructor]
        public EventPage(int ignoreThis)
        {
            MovementType = 0;
            MovementSpeed = 2;
            MovementFreq = 2;
            Passable = 0;
            Layer = 1;
            Trigger = 0;
            HideName = 0;;
        }

        public EventPage(ByteBuffer curBuffer)
        {
            Desc = curBuffer.ReadString();
            MovementType = curBuffer.ReadInteger();
            if (MovementType == 2) MoveRoute.Load(curBuffer);
            MovementSpeed = curBuffer.ReadInteger();
            MovementFreq = curBuffer.ReadInteger();
            Passable = curBuffer.ReadInteger();
            Layer = curBuffer.ReadInteger();
            Trigger = curBuffer.ReadInteger();
            TriggerVal = curBuffer.ReadGuid();
            TriggerCommand = curBuffer.ReadString();
            FaceGraphic = curBuffer.ReadString();
            Graphic.Load(curBuffer);
            HideName = curBuffer.ReadInteger();
            DisablePreview = curBuffer.ReadInteger();
            DirectionFix = curBuffer.ReadInteger();
            WalkingAnimation = curBuffer.ReadInteger();
            AnimationId = curBuffer.ReadGuid();
            InteractionFreeze = curBuffer.ReadInteger();
            var x = curBuffer.ReadInteger();
            for (var i = 0; i < x; i++)
            {
                CommandLists.Add(new CommandList(curBuffer));
            }
            ConditionLists.Load(curBuffer);
        }

        public void WriteBytes(ByteBuffer myBuffer)
        {
            myBuffer.WriteString(Desc);
            myBuffer.WriteInteger(MovementType);
            if (MovementType == 2) MoveRoute.Save(myBuffer);
            myBuffer.WriteInteger(MovementSpeed);
            myBuffer.WriteInteger(MovementFreq);
            myBuffer.WriteInteger(Passable);
            myBuffer.WriteInteger(Layer);
            myBuffer.WriteInteger(Trigger);
            myBuffer.WriteGuid(TriggerVal);
            myBuffer.WriteString(TriggerCommand);
            myBuffer.WriteString(TextUtils.SanitizeNone(FaceGraphic));
            Graphic.Save(myBuffer);
            myBuffer.WriteInteger(HideName);
            myBuffer.WriteInteger(DisablePreview);
            myBuffer.WriteInteger(DirectionFix);
            myBuffer.WriteInteger(WalkingAnimation);
            myBuffer.WriteGuid(AnimationId);
            myBuffer.WriteInteger(InteractionFreeze);
            myBuffer.WriteInteger(CommandLists.Count);
            foreach (var commandList in CommandLists)
            {
                commandList.WriteBytes(myBuffer);
            }
            ConditionLists.Save(myBuffer);
        }
    }
}