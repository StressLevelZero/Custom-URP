using SLZ.SLZEditorTools;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.SLZMaterialUI
{
    public class UIEMessageBox : VisualElement
    {
        public Button button;
        public UIEMessageBox(MessageType messageType, string message)
        {
            AddToClassList("messageBox");
            this.style.flexDirection = FlexDirection.Row;
            Texture2D icon;
            switch (messageType)
            {
                case MessageType.Warning: icon = ShaderGUIUtils.GetClosestUnityIconMip("console.warnicon", 32); break;
                case MessageType.Error: icon = ShaderGUIUtils.GetClosestUnityIconMip("console.erroricon", 32); break;
                default: icon = ShaderGUIUtils.GetClosestUnityIconMip("console.infoicon", 32); break;
            }
            Image messageIcon = new Image();
            messageIcon.image = icon;
            messageIcon.style.minWidth = 32;
            messageIcon.style.maxWidth = 32;
            messageIcon.style.minHeight = 32;
            messageIcon.style.maxHeight = 32;
            messageIcon.style.alignContent = Align.Center;
            messageIcon.style.alignItems = Align.Center;
            this.Add(messageIcon);
            Label messageLabel = new Label(message);
            messageLabel.style.alignSelf = Align.Center;
            this.Add(messageLabel);
        }

        public UIEMessageBox(MessageType messageType, string message, string buttonText, Action buttonAction)
        {
            AddToClassList("messageBox");
            this.style.flexDirection = FlexDirection.Row;
            this.style.justifyContent = Justify.SpaceBetween;
            VisualElement leftBox = new VisualElement();
            leftBox.style.flexDirection = FlexDirection.Row;
            leftBox.style.flexGrow = 1;
            leftBox.style.flexShrink = 1;
            VisualElement rightBox = new VisualElement();

            Texture2D icon;
            switch (messageType)
            {
                case MessageType.Warning: icon = ShaderGUIUtils.GetClosestUnityIconMip("console.warnicon", 32); break;
                case MessageType.Error: icon = ShaderGUIUtils.GetClosestUnityIconMip("console.erroricon", 32); break;
                default: icon = ShaderGUIUtils.GetClosestUnityIconMip("console.infoicon", 32); break;
            }
            Image messageIcon = new Image();
            messageIcon.image = icon;
            messageIcon.style.minWidth = 32;
            messageIcon.style.maxWidth = 32;
            messageIcon.style.minHeight = 32;
            messageIcon.style.maxHeight = 32;
            messageIcon.style.alignContent = Align.Center;
            messageIcon.style.alignItems = Align.Center;
            leftBox.Add(messageIcon);
            Label messageLabel = new Label(message);
            messageLabel.style.alignSelf = Align.Center;
            leftBox.Add(messageLabel);
            this.Add(leftBox);
            leftBox.style.flexGrow = 1;

            rightBox.style.flexDirection = FlexDirection.Row;
            button = new Button(buttonAction);
            button.style.height = 16;
            button.text = buttonText;
            button.style.alignSelf = Align.Center;
            rightBox.Add(button);
            this.Add(rightBox);
        }
    }
}