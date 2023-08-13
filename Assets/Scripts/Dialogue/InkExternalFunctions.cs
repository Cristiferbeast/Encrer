using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Ink.Runtime;

public class InkExternalFunctions
{
    public void Bind(Story story, Animator emoteAnimator)
    {
        story.BindExternalFunction("playEmote", (string emoteName) => PlayEmote(emoteName, emoteAnimator));
        story.BindExternalFunction("background", (string backgroundname) => background(backgroundname));
        story.BindExternalFunction("portraitstate", (bool state) => showportrait(state));
    }

    public void Unbind(Story story) 
    {
        story.UnbindExternalFunction("playEmote");
        story.UnbindExternalFunction("background");
    }

    public void PlayEmote(string emoteName, Animator emoteAnimator)
    {
        if (emoteAnimator != null) 
        {
            emoteAnimator.Play(emoteName);
        }
        else 
        {
            Debug.LogWarning("Tried to play emote, but emote animator was "
                + "not initialized when entering dialogue mode.");
        }
    }
    //Encrer Advanced Library
    public static void background(string imagename)
    {
        DialogueManager d = DialogueManager.GetInstance();
        GameObject targetObject = d.background;
        if (targetObject == null) {
            Debug.LogWarning("Target object not assigned.");
            return;
        }
        SpriteRenderer spriteRenderer = targetObject.GetComponent<SpriteRenderer>();
        Sprite[] sprites = d.backgroundSprite;
        foreach (Sprite sprite in sprites)
        {
            if (sprite.name == imagename)
            {
                spriteRenderer.sprite = sprite;
                break; 
            }
        }
    }
    public static void showportrait(bool state){
        DialogueManager d = DialogueManager.GetInstance();
        GameObject frame = d.portraitFrame;
        GameObject portrait = d.portrait;
        portrait.SetActive(state);
        frame.SetActive(state);
    }
}