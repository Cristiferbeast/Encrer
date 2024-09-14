using Ink.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InkExternalFunctions
{
    public void Bind(Story story, Animator emoteAnimator)
    {
        story.BindExternalFunction("playEmote", (string emoteName) => PlayEmote(emoteName, emoteAnimator));
        story.BindExternalFunction("background", (string backgroundname) => background(backgroundname));
        story.BindExternalFunction("portraitstate", (bool state, int input) => showportrait(state, input));
        story.BindExternalFunction("portraitImage", (int position, string imagename) => portraitImage(position, imagename));
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
    public static void portraitImage(int position, string imagename)
    {
        try
        {
            DialogueManager d = DialogueManager.GetInstance();
            GameObject portrait;
            switch (position)
            {
                case 1:
                    portrait = d.portraitleft;
                    break;
                case 2:
                    portrait = d.portraitright;
                    break;
                default:
                    portrait = d.portraitleftcenter;
                    break;
            }
            SpriteRenderer portraitImage = portrait.GetComponent<SpriteRenderer>();
            Sprite[] sprites = d.portraitSprites;
            bool spriteFound = false;
            foreach (Sprite sprite in sprites)
            {
                Debug.Log("Loaded Sprite: " + sprite.name);
                if (sprite.name == imagename)
                {
                    portraitImage.sprite = sprite;
                    spriteFound = true;
                    break;
                }
            }
            if (!spriteFound)
            {
                Debug.LogError("Did not Find Sprite with name: " + imagename);
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
        }
    }
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
    public static void FullStart(bool start)
    {
        //This is a temporary fix due to some issues occuring with Portraits, Portrait Reform is needed for the next update
        showportrait(start);
        showportrait(start, 2);
        showportrait(start, 3);
    }
    public static void showportrait(bool state, int input = 1){
        DialogueManager d = DialogueManager.GetInstance();
        GameObject portrait;
        switch (input) {
            case 1:
                portrait = d.portraitleft;
                break;
            case 2:
                portrait = d.portraitright;
                break;
            case 3:
                portrait = d.portraitleftcenter;
                break;
            default:
                portrait = d.portraitleft;
                break;
        }
        portrait.SetActive(state);
    }
    public static void showrightportrait(bool state)
    {
        DialogueManager d = DialogueManager.GetInstance();
        GameObject portrait = d.portraitright;
        portrait.SetActive(state);
    }
}