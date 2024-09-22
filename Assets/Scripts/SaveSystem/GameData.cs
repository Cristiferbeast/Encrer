using Ink.Runtime;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GameData
{
    public string knot = "";

    public GameData(Ink.Runtime.Story story)
    {
        knot = story.state.currentPathString.Substring(0, story.state.currentPathString.IndexOf("."));
    }

    public GameData()
    {
        knot = "";
    }
}
