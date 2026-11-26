// Android SDK introduces Android.Widget.Button / CheckBox and Android.App.Dialog.
// Intersect's existing UI code refers to its Gwen and event types unqualified.
// Keep the original meanings in the Android-only compile of Client.Core.
global using Button = Intersect.Client.Framework.Gwen.Control.Button;
global using CheckBox = Intersect.Client.Framework.Gwen.Control.CheckBox;
global using Dialog = Intersect.Client.Entities.Events.Dialog;
