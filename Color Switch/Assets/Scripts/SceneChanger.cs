using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    public string nivel;

    public void ChangeScene(string name)
    {
        SceneManager.LoadScene(name);
    }

    public void Exit()
    {
        Application.Quit();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ChangeScene(nivel);
    }
}
