using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor.PackageManager;
using UnityEngine;

public static class SaveSystem
{
    public static void SaveGame(Ink.Runtime.Story story)
    {
        BinaryFormatter formatter = new BinaryFormatter();
        string path = Application.persistentDataPath + "/game.save";
        FileStream stream = new FileStream(path, FileMode.Create);
        GameData data = new GameData(story);
        formatter.Serialize(stream, data);
        stream.Close();
    }

    public static GameData LoadGame()
    {
        string path = Application.persistentDataPath + "/game.save";
        if (File.Exists(path))
        {
            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream(path, FileMode.Open);
            GameData data = new GameData();
            try
            {
                data = formatter.Deserialize(stream) as GameData;
                stream.Close();
                return data;
            }
            catch
            {
                stream.Close();
                return null;
            }
        }
        else
        {
            return null;
        }
    }
}
