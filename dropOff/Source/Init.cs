using System.IO;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using UnityEngine;

//Harmony entry point.
public class dropOffModApi : IModApi
{
    public void InitMod(Mod modInstance)
    {
        //Load hotkeys from dropOffConfig.xml
        try
        {
            string path = GamePrefs.GetString(EnumGamePrefs.UserDataFolder) + "/Mods/dropOff";
            if (!Directory.Exists(path))
                path = Directory.GetCurrentDirectory() + "/Mods/dropOff";

            XmlDocument xml = new XmlDocument();
            xml.Load(path + "/dropOffConfig.xml");

            string[] dropOffLockButtons = xml.GetElementsByTagName("DropOffLockButtons")[0].InnerText.Split(' ');
            DropOff.dropOffLockHotkeys = new KeyCode[dropOffLockButtons.Length];
            for (int i = 0; i < dropOffLockButtons.Length; i++)
                DropOff.dropOffLockHotkeys[i] = (KeyCode)int.Parse(dropOffLockButtons[i]);

            string[] dropOffButtons = xml.GetElementsByTagName("dropOffButtons")[0].InnerText.Split(' ');
            DropOff.dropOffHotkeys = new KeyCode[dropOffButtons.Length];
            for (int i = 0; i < dropOffButtons.Length; i++)
                DropOff.dropOffHotkeys[i] = (KeyCode)int.Parse(dropOffButtons[i]);
        }
        catch
        {
            Log.Error("Failed to load or parse config for dropOff");

            DropOff.dropOffLockHotkeys = new KeyCode[1];
            DropOff.dropOffLockHotkeys[0] = KeyCode.LeftAlt;

            DropOff.dropOffHotkeys = new KeyCode[2];
            DropOff.dropOffHotkeys[0] = KeyCode.LeftAlt;
            DropOff.dropOffHotkeys[1] = KeyCode.X;
        }

        Harmony harmony = new Harmony(GetType().ToString());
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}

