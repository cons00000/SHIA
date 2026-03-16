using UnityEngine;
using static System.Math;

public class TargetControl : MonoBehaviour
{

    public Transform agent;       //The agent that looks at target
    public float rotateSpeed;     //Speed of head rotation
    public float headTilt;        //To set head tilt (dY)

    private float actualThetaX; //Angle horizontal actuel
    private float thetaTargetX; //Angle objectif horizontal
    private float originalThetaX; //Angle depart de mouvement horizontal

    private float actualThetaY; //Angle vertical actuel
    private float thetaTargetY; //Angle objectif vertical
    private float originalThetaY; //Angle depart de mouvement vertical

    private bool lookAtTarget;

    private float originalTheta;

    // Start is called before the first frame update
    void Start()
    {
        transform.position = new Vector3(agent.transform.position.x, agent.transform.position.y + 1.4f, agent.transform.position.z - 10f);
        actualThetaX = 0f;
        thetaTargetX = 0f;
        actualThetaY = 0f;
        thetaTargetY = 0f;
        lookAtTarget = true;
    }

    // Update is called once per frame
    void Update()
    {
        //Control with arrow keys
        if (Input.GetKeyDown(KeyCode.RightArrow)) //To 45� (+30�)
        {
            turnToObjective((float)PI / 6f, 0f);
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow)) //To -45� (-30�)
        {
            turnToObjective((float)-PI / 6f, 0f);
        }

        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            turnToObjective(0f, (float)-PI / 8f);
        }

        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            turnToObjective(0f, 0f);
        }


        moveHead();
    }


    /**
     * Place la cible du regard pour une rotation X et Z fix�s
     */
    public void turnToObjective(float new_thetaTargetX, float new_thetaTargetY)
    {
        lookAtTarget = false;
        thetaTargetX = ((float)PI * new_thetaTargetX) / 180f;
        thetaTargetY = ((float)PI * new_thetaTargetY) / 180f;
        originalThetaX = actualThetaX;
        originalThetaY = actualThetaY;
    }

    /**
     * Actualisation mouvement de la tete
     */
    void moveHead()
    {
        if (!lookAtTarget)
        {
            bool lookAtTargetX = false;
            float smoothX = (0.01f + Abs(thetaTargetX - actualThetaX)) / (0.01f + Abs(thetaTargetX - originalThetaX)) + 0.1f;
            float dthetaX = rotateSpeed * Time.deltaTime * smoothX;
            if (thetaTargetX < actualThetaX)
            {
                //Update mouvement / utilisation de la progression pour mouvement non lin�aire et plus naturel.
                actualThetaX -= dthetaX;
                if (actualThetaX < thetaTargetX)
                {
                    actualThetaX = thetaTargetX;
                    lookAtTargetX = true;
                }
            }
            else
            {
                actualThetaX += dthetaX;
                if (actualThetaX > thetaTargetX)
                {
                    actualThetaX = thetaTargetX;
                    lookAtTargetX = true;
                }
            }

            bool lookAtTargetY = false;
            float smoothY = (0.01f + Abs(thetaTargetY - actualThetaY)) / (0.01f + Abs(thetaTargetY - originalThetaY)) + 0.1f;
            float dthetaY = 0.5f * rotateSpeed * Time.deltaTime * smoothX;
            if (thetaTargetY < actualThetaY)
            {
                //Update mouvement / utilisation de la progression pour mouvement non lin�aire et plus naturel.
                actualThetaY -= dthetaY;
                if (actualThetaY < thetaTargetY)
                {
                    actualThetaY = thetaTargetY;
                    lookAtTargetY = true;
                }
            }
            else
            {
                actualThetaY += dthetaY;
                if (actualThetaY > thetaTargetY)
                {
                    actualThetaY = thetaTargetY;
                    lookAtTargetY = true;
                }
            }

            lookAtTarget = lookAtTargetX && lookAtTargetY;
            transform.position = new Vector3((float)(10 * Sin(actualThetaX)), (float)(10 * Sin(actualThetaY)) + 1.4f, (float)(-10 * Cos(actualThetaX)));
        }

    }



}
