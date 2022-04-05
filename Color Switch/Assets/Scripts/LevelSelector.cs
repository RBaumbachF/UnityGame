using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelSelector : MonoBehaviour
{
    public void levelToLoad(string level)
    {
        SceneManager.LoadScene(level);
    }
}
