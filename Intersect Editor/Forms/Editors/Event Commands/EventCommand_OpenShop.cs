﻿using System;
using System.Windows.Forms;
using Intersect.Editor.Core;
using Intersect.Editor.Localization;
using Intersect.Enums;
using Intersect.GameObjects.Events;

namespace Intersect.Editor.Forms.Editors.Event_Commands
{
    public partial class EventCommandOpenShop : UserControl
    {
        private readonly FrmEvent mEventEditor;
        private EventCommand mMyCommand;

        public EventCommandOpenShop(EventCommand refCommand, FrmEvent editor)
        {
            InitializeComponent();
            mMyCommand = refCommand;
            mEventEditor = editor;
            InitLocalization();
            cmbShop.Items.Clear();
            cmbShop.Items.AddRange(Database.GetGameObjectList(GameObjectType.Shop));
            cmbShop.SelectedIndex = Database.GameObjectListIndex(GameObjectType.Shop, mMyCommand.Guids[0]);
        }

        private void InitLocalization()
        {
            grpShop.Text = Strings.EventOpenShop.title;
            lblShop.Text = Strings.EventOpenShop.label;
            btnSave.Text = Strings.EventOpenShop.okay;
            btnCancel.Text = Strings.EventOpenShop.cancel;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (cmbShop.SelectedIndex > -1)
                mMyCommand.Guids[0] = Database.GameObjectIdFromList(GameObjectType.Shop, cmbShop.SelectedIndex);
            mEventEditor.FinishCommandEdit();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            mEventEditor.CancelCommandEdit();
        }
    }
}