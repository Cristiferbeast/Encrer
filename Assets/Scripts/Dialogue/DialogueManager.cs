using Ink.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    [Header("Params")]
    [SerializeField] private float typingSpeed = 0.04f;

    [Header("Load Globals JSON")]
    [SerializeField] private TextAsset loadGlobalsJSON;

    [Header("Dialogue UI")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private GameObject continueIcon;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private TextMeshProUGUI displayNameText;
    [SerializeField] private Animator portraitAnimator;
    private Animator layoutAnimator;

    [Header("Choices UI")]
    [SerializeField] private GameObject[] choices;
    private TextMeshProUGUI[] choicesText;

    [Header("Audio")]
    [SerializeField] private DialogueAudioInfoSO defaultAudioInfo;
    [SerializeField] private DialogueAudioInfoSO[] audioInfos;
    [SerializeField] private bool makePredictable;
    [SerializeField] private bool muted = true;
    private DialogueAudioInfoSO currentAudioInfo;
    private Dictionary<string, DialogueAudioInfoSO> audioInfoDictionary;
    private AudioSource audioSource;

    [Header("Emote Animator")]
    [SerializeField] private Animator emoteAnimator;

    [Header("Ink JSON")]
    [SerializeField] private TextAsset inkJSON;

    [Header("Backgrounds")]
    public GameObject background;
    public Sprite[] backgroundSprite;

    [Header("Portrait Control")]
    public GameObject portraitleft;
    public GameObject portraitright;
    public GameObject portraitleftcenter;
    public Sprite[] portraitSprites;

    [Header("General Control")]
    public GameObject generalCanvas;
    public GameObject menuCanvas;
    public GameObject imageObjects;

    private Story currentStory;
    public bool dialogueIsPlaying { get; private set; }

    private bool canContinueToNextLine = false;

    private Coroutine displayLineCoroutine;

    private static DialogueManager instance;

    //Tags
    private const string SPEAKER_TAG = "speaker";
    private const string PORTRAITLEFT_TAG = "portrait";
    private const string PORTRAITRIGHT_TAG = "pright";
    private const string AUDIO_TAG = "audio";
    private const string FONT_TAG = "font";

    //Variables and External Functions
    private DialogueVariables dialogueVariables;
    private InkExternalFunctions inkExternalFunctions;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("Found more than one Dialogue Manager in the scene");
        }
        instance = this;

        dialogueVariables = new DialogueVariables(loadGlobalsJSON);
        inkExternalFunctions = new InkExternalFunctions();

        audioSource = this.gameObject.AddComponent<AudioSource>();
        currentAudioInfo = defaultAudioInfo;

    }
    public static DialogueManager GetInstance()
    {
        return instance;
    }
    private void Start()
    {
        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);

        // get all of the choices text 
        choicesText = new TextMeshProUGUI[choices.Length];
        int index = 0;
        foreach (GameObject choice in choices)
        {
            choicesText[index] = choice.GetComponentInChildren<TextMeshProUGUI>();
            index++;
        }

        if (!muted) InitializeAudioInfoDictionary(); //If Muted we dont need to load the Audios at Start, we Can Do This Whenever Its Unmunted
        DialogueManager.GetInstance().EnterDialogueMode(inkJSON, emoteAnimator);
    }
    private void InitializeAudioInfoDictionary()
    {
        audioInfoDictionary = new Dictionary<string, DialogueAudioInfoSO>();
        audioInfoDictionary.Add(defaultAudioInfo.id, defaultAudioInfo);
        foreach (DialogueAudioInfoSO audioInfo in audioInfos)
        {
            audioInfoDictionary.Add(audioInfo.id, audioInfo);
        }
    }
    private void SetCurrentAudioInfo(string id)
    {
        DialogueAudioInfoSO audioInfo = null;
        audioInfoDictionary.TryGetValue(id, out audioInfo);
        if (audioInfo != null)
        {
            this.currentAudioInfo = audioInfo;
        }
        else
        {
            Debug.LogWarning("Failed to find audio info for id: " + id);
        }
    }
    private void Update()
    {
        if (!dialogueIsPlaying)
        {
            //If Dialogue Isn't Playing then Return
            return;
        }

        // NOTE: The 'currentStory.currentChoiecs.Count == 0' part was to fix a bug in System 7 (Encrer does intend on finding a more permanent fix)
        if (canContinueToNextLine && currentStory.currentChoices.Count == 0 && CheckInput())
        {
            ContinueStory();
        }
    }
    public void EnterDialogueMode(TextAsset inkJSON, Animator emoteAnimator)
    {
        currentStory = new Story(inkJSON.text);
        dialogueIsPlaying = true;
        dialoguePanel.SetActive(true);

        dialogueVariables.StartListening(currentStory);
        inkExternalFunctions.Bind(currentStory, emoteAnimator);

        // When Entering Dialogue Mode we Have to Reset All Active Toggles
        InkExternalFunctions.showportrait(false);
        displayNameText.text = "???";
        ContinueStory();
    }
    private IEnumerator ExitDialogueMode()
    {
        yield return new WaitForSeconds(0.2f);

        dialogueVariables.StopListening(currentStory);
        inkExternalFunctions.Unbind(currentStory);

        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);
        dialogueText.text = "";

        if (!muted) SetCurrentAudioInfo(defaultAudioInfo.id); // Go back to default audio if Unmunted
    }
    private void ContinueStory()
    {
        try
        {
            if (!currentStory.canContinue) StartCoroutine(ExitDialogueMode()); //If the Story Can't Continue Then Exit the Dialogue 
            else if (displayLineCoroutine != null) StopCoroutine(displayLineCoroutine); // This is from System 7 its exact role is stated as "set text for the current dialogue line"
            string nextLine = currentStory.Continue();
            if (nextLine.Equals("") && !currentStory.canContinue) StartCoroutine(ExitDialogueMode()); // Handle case where the last line is an external function
            else  // Normal Cases where there are No Issues
            {
                HandleTags(currentStory.currentTags);
                displayLineCoroutine = StartCoroutine(DisplayLine(nextLine));
            }
        }
        catch
        {
            Debug.LogError("Issue With Continuing Story, Ensure that Story Construction is Proper, Or That Endings are Proper");
            return;
        }
    }
    private IEnumerator DisplayLine(string line)
    {
        dialogueText.text = line;
        dialogueText.maxVisibleCharacters = 0; // set the text to the full line, but set the visible characters to 0, 
        continueIcon.SetActive(false); // hide items while text is typing
        HideChoices();
        canContinueToNextLine = false;
        bool isAddingRichTextTag = false;

        foreach (char letter in line.ToCharArray()) // display each letter one at a time
        {
            if (CheckInput()) // if the submit button is pressed, finish up displaying the line right away
            {
                dialogueText.maxVisibleCharacters = line.Length;
                break;
            }
            if (letter == '<' || isAddingRichTextTag)  // check for rich text tag, if found, add it without waiting
            {
                isAddingRichTextTag = true;
                if (letter == '>')
                {
                    isAddingRichTextTag = false;
                }
            }
            else // if not rich text, add the next letter and wait a small time
            {
                PlayDialogueSound(dialogueText.maxVisibleCharacters, dialogueText.text[dialogueText.maxVisibleCharacters], muted);
                dialogueText.maxVisibleCharacters++;
                yield return new WaitForSeconds(typingSpeed);
            }
        }
        // Sets Up Actions after the Entire Line is Displayed 
        continueIcon.SetActive(true);
        DisplayChoices();
        canContinueToNextLine = true;
    }
    private void PlayDialogueSound(int currentDisplayedCharacterCount, char currentCharacter, bool muted)
    {
        if (muted)
        {
            return;
        }
        // set variables for the below based on our config
        AudioClip[] dialogueTypingSoundClips = currentAudioInfo.dialogueTypingSoundClips;
        int frequencyLevel = currentAudioInfo.frequencyLevel;
        float minPitch = currentAudioInfo.minPitch;
        float maxPitch = currentAudioInfo.maxPitch;
        bool stopAudioSource = currentAudioInfo.stopAudioSource;

        // play the sound based on the config
        if (currentDisplayedCharacterCount % frequencyLevel == 0)
        {
            if (stopAudioSource)
            {
                audioSource.Stop();
            }
            AudioClip soundClip = null;
            // create predictable audio from hashing
            if (makePredictable)
            {
                int hashCode = currentCharacter.GetHashCode();
                // sound clip
                int predictableIndex = hashCode % dialogueTypingSoundClips.Length;
                soundClip = dialogueTypingSoundClips[predictableIndex];
                // pitch
                int minPitchInt = (int)(minPitch * 100);
                int maxPitchInt = (int)(maxPitch * 100);
                int pitchRangeInt = maxPitchInt - minPitchInt;
                // cannot divide by 0, so if there is no range then skip the selection
                if (pitchRangeInt != 0)
                {
                    int predictablePitchInt = (hashCode % pitchRangeInt) + minPitchInt;
                    float predictablePitch = predictablePitchInt / 100f;
                    audioSource.pitch = predictablePitch;
                }
                else
                {
                    audioSource.pitch = minPitch;
                }
            }
            // otherwise, randomize the audio
            else
            {
                // sound clip
                int randomIndex = UnityEngine.Random.Range(0, dialogueTypingSoundClips.Length);
                soundClip = dialogueTypingSoundClips[randomIndex];
                // pitch
                audioSource.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            }

            // play sound
            audioSource.PlayOneShot(soundClip);
        }
    }
    private void HideChoices()
    {
        foreach (GameObject choiceButton in choices)
        {
            choiceButton.SetActive(false);
        }
    }
    private void HandleTags(List<string> currentTags)
    {
        foreach (string tag in currentTags) // loop through each tag and handle it accordingly
        {
            // parse the tag
            string[] splitTag = tag.Split(':');
            if (splitTag.Length != 2) { Debug.LogError("Tag could not be appropriately parsed: " + tag); }
            string tagKey = splitTag[0].Trim();
            string tagValue = splitTag[1].Trim();

            switch (tagKey) // handle the tag
            {
                case SPEAKER_TAG:
                    displayNameText.text = tagValue;
                    break;
                case PORTRAITLEFT_TAG:
                    InkExternalFunctions.showportrait(true);
                    SpriteRenderer portraitImage = portraitleft.GetComponent<SpriteRenderer>();
                    Sprite[] sprites = portraitSprites;
                    bool spriteFound = false;
                    foreach (Sprite sprite in sprites)
                    {
                        Debug.Log("Loaded Sprite: " + sprite.name);
                        if (sprite.name == tagValue)
                        {
                            portraitImage.sprite = sprite;
                            spriteFound = true;
                            break;
                        }
                    }
                    if (!spriteFound)
                    {
                        Debug.LogError("Did not Find Sprite with name: " + tagValue);
                    }
                    break;
                case PORTRAITRIGHT_TAG:
                    InkExternalFunctions.showportrait(true);
                    portraitImage = portraitright.GetComponent<SpriteRenderer>();
                    sprites = portraitSprites;
                    spriteFound = false;
                    foreach (Sprite sprite in sprites)
                    {
                        Debug.Log("Loded Sprite: " + sprite.name);
                        if (sprite.name == tagValue)
                        {
                            portraitImage.sprite = sprite;
                            spriteFound = true;
                            break;
                        }
                    }
                    if (!spriteFound)
                    {
                        Debug.LogError("Did not Find Sprite with name: " + tagValue);
                    }
                    break;
                case AUDIO_TAG:
                    SetCurrentAudioInfo(tagValue);
                    break;
                case FONT_TAG:
                    if (float.TryParse(tagValue, out float result))
                    {
                        displayNameText.fontSize = result;
                    }
                    else { Debug.LogWarning("Font Tag did not have a Float Extension"); }
                    break;
                default:
                    Debug.LogWarning("Tag came in but is not currently being handled: " + tag);
                    break;
            }
        }
    }
    private void DisplayChoices()
    {
        List<Choice> currentChoices = currentStory.currentChoices;
        if (currentChoices.Count > choices.Length) //To Ensure that Encrer doesnt get overloaded
        {
            Debug.LogError("More choices were given than the UI can support. Number of choices given: "
                + currentChoices.Count);
        }
        int index = 0;
        foreach (Choice choice in currentChoices) //Set Up the Choices 
        {
            choices[index].gameObject.SetActive(true);
            choicesText[index].text = choice.text;
            index++;
        }
        for (int i = index; i < choices.Length; i++) //Ensure Unused Parts are Hidden
        {
            choices[i].gameObject.SetActive(false);
        }
        StartCoroutine(SelectFirstChoice());
    }
    private IEnumerator SelectFirstChoice()
    {
        //To be frank i have no fucking clue how this works Y^Y, still working on that, but its a relic of System 7
        // Event System requires we clear it first, then wait
        // for at least one frame before we set the current selected object.
        EventSystem.current.SetSelectedGameObject(null);
        yield return new WaitForEndOfFrame();
        EventSystem.current.SetSelectedGameObject(choices[0].gameObject);
    }
    public void MakeChoice(int choiceIndex)
    {
        if (canContinueToNextLine)
        {
            currentStory.ChooseChoiceIndex(choiceIndex);
            ContinueStory();
        }
    }
    public Ink.Runtime.Object GetVariableState(string variableName)
    {
        Ink.Runtime.Object variableValue = null;
        dialogueVariables.variables.TryGetValue(variableName, out variableValue);
        if (variableValue == null)
        {
            Debug.LogWarning("Ink Variable was found to be null: " + variableName);
        }
        return variableValue;
    }
    public void SettingsButton(bool state) //The Settings Menu is controlled from this button
    {
        this.generalCanvas.SetActive(!state);
        this.imageObjects.SetActive(!state);
        this.menuCanvas.SetActive(state);
        Debug.Log("Settings Button Clicked");
    }
    public bool CheckInput()
    {
        bool spaceKeyPressed = false;
        bool leftMouseButtonPressed = false;
        bool touchStarted = false;
        if (Keyboard.current != null) spaceKeyPressed = Keyboard.current.spaceKey.wasPressedThisFrame;
        if (Mouse.current != null) leftMouseButtonPressed = Mouse.current.leftButton.wasPressedThisFrame;
        if (Touchscreen.current != null) touchStarted = Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
        return spaceKeyPressed || leftMouseButtonPressed || touchStarted;
    }
    public void OnApplicationQuit()
    {
        // This method will get called anytime the application exits.
        // Depending on your game, you may want to save variable state in other places.
        dialogueVariables.SaveVariables();
    }

}