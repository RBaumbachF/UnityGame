using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Player : MonoBehaviour
{
    public float jumpForce = 10f;

    public Rigidbody2D rb;

    public SpriteRenderer spriteRenderer;

    public string currentColor, previousColor;

    public Color colorBlue, colorYellow, colorPink, colorPurple;

    void Start()
    {
        SetRandomColor();
    }

    void Update()
    {
        if (Input.GetButtonDown("Jump") || Input.GetMouseButtonDown(0))
        {
            rb.velocity = Vector2.up * jumpForce;
        }
    }

    void OnTriggerEnter2D(Collider2D collider2D)
    {
        if (collider2D.tag == "ColorChanger")
        {
            SetRandomColor();
            Destroy(collider2D.gameObject);
            return;
        }

        if (collider2D.tag != currentColor)
        {
            Debug.Log("GAME OVER");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    void SetRandomColor()
    {
        int index = Random.Range(0, 4);
        switch (index)
        {
            case 0:
                currentColor = "Blue";
                spriteRenderer.color = colorBlue;
                break;
            case 1:
                currentColor = "Yellow";
                spriteRenderer.color = colorYellow;
                break;
            case 2:
                currentColor = "Pink";
                spriteRenderer.color = colorPink;
                break;
            case 3:
                currentColor = "Purple";
                spriteRenderer.color = colorPurple;
                break;
        }

        if (previousColor == currentColor)
        {
            SetRandomColor();
        }

        previousColor= currentColor;
    }
}
