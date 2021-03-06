﻿using UnityEngine;
using System.Collections;
using BulletUnity;
using BulletSharp;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.FEA;
using Assets.Scripts.BUExtensions;

/// <summary>
/// To be attached to all robot parent objects.
/// Handles all robot-specific interaction such as driving joints, resetting, and orienting robot.
/// </summary>
public class Robot : MonoBehaviour
{

    private bool isInitialized;

    private const float ResetVelocity = 0.05f;
    private const int SolverIterations = 100;

    private RigidNode_Base rootNode;

    private Vector3 robotStartPosition = new Vector3(01f, 1f, 0f);
    private BulletSharp.Math.Matrix robotStartOrientation = BulletSharp.Math.Matrix.Identity;

    private List<GameObject> extraElements;

    private UnityPacket unityPacket;

    private bool isResettingOrientation;
    public bool IsResetting = false;

    private DriverPracticeRobot dpmRobot;

    public bool ControlsEnabled = true;

    private Vector3 nodeToRobotOffset;

    private MainState mainState;

    public UnityPacket.OutputStatePacket Packet;

    public int controlIndex = 0;

    private const float HOLD_TIME = 0.8f;
    private float keyDownTime = 0f;

    public string RobotName;

    private RobotCameraManager robotCameraManager;

    private GameObject manipulatorObject;
    private RigidNode_Base manipulatorNode;

    UnityPacket.OutputStatePacket.DIOModule[] emptyDIO = new UnityPacket.OutputStatePacket.DIOModule[2];

    // Use this for initialization
    void Start()
    {
    }

    /// <summary>
    /// Called once per frame to ensure all rigid bodie components are activated
    /// </summary>
    void Update()
    {

        BRigidBody rigidBody = GetComponentInChildren<BRigidBody>();

        if (!rigidBody.GetCollisionObject().IsActive)
            rigidBody.GetCollisionObject().Activate();
        if (!IsResetting)
        {
            if (InputControl.GetButtonDown(Controls.buttons[controlIndex].resetRobot))
            {
                keyDownTime = Time.time;
            }

            else if (InputControl.GetButton(Controls.buttons[controlIndex].resetRobot))
            {
                if (Time.time - keyDownTime > HOLD_TIME)
                {
                    IsResetting = true;
                    BeginReset();
                }
            }

            else if (InputControl.GetButtonUp(Controls.buttons[controlIndex].resetRobot))
            {
                BeginReset();
                EndReset();
            }
        }

    }
    /// <summary>
    /// Called once every physics step (framerate independent) to drive motor joints as well as handle the resetting of the robot
    /// </summary>
    void FixedUpdate()
    {
        if (rootNode != null && ControlsEnabled)
        {

            if (Packet != null) DriveJoints.UpdateAllMotors(rootNode, Packet.dio, controlIndex, MixAndMatchMode.GetMecanum());
            else DriveJoints.UpdateAllMotors(rootNode, emptyDIO, controlIndex, MixAndMatchMode.GetMecanum());
            int isMixAndMatch = PlayerPrefs.GetInt("MixAndMatch", 0); //0 is false, 1 is true
           
            int isManipulator = PlayerPrefs.GetInt("hasManipulator", 0); //0 is false, 1 is true
            
            //If the robot is in Mix and Match mode and has a manipulator, update the manipulator motors
            if (isManipulator == 1 && isMixAndMatch == 1)
            {
                DriveJoints.UpdateManipulatorMotors(manipulatorNode, emptyDIO, controlIndex, MixAndMatchMode.GetMecanum());
            }
        }

        if (IsResetting)
        {
            Resetting();
        }
    }

    /// <summary>
    /// Initializes physical robot based off of robot directory.
    /// </summary>
    /// <param name="directory">folder directory of robot</param>
    /// <returns></returns>
    public bool InitializeRobot(string directory, MainState source)
    {
        
        //Deletes all nodes if any exist, take the old node transforms out from the robot object
        int childCount = transform.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            //If not do this the game object is destroyed but the parent-child transform relationship remains!
            child.parent = null;
            Destroy(child.gameObject);
        }
        SensorManager sensorManager = GameObject.Find("SensorManager").GetComponent<SensorManager>();
        sensorManager.ResetSensorLists();

        mainState = source;
        transform.position = robotStartPosition;

        RigidNode_Base.NODE_FACTORY = delegate (Guid guid)
        {
            return new RigidNode(guid);
        };

        List<RigidNode_Base> nodes = new List<RigidNode_Base>();
        //Read .robot instead. Maybe need a RobotSkeleton class
        rootNode = BXDJSkeleton.ReadSkeleton(directory + "\\skeleton.bxdj");
        rootNode.ListAllNodes(nodes);

        int numWheels = nodes.Count(x => x.HasDriverMeta<WheelDriverMeta>() && x.GetDriverMeta<WheelDriverMeta>().type != WheelType.NOT_A_WHEEL);
        float collectiveMass = 0f;

        foreach (RigidNode_Base n in nodes)
        {
            RigidNode node = (RigidNode)n;
            node.CreateTransform(transform);

            if (!node.CreateMesh(directory + "\\" + node.ModelFileName))
            {
                Debug.Log("Robot not loaded!");
                return false;
            }

            node.CreateJoint(numWheels);

            if (node.PhysicalProperties != null)
                collectiveMass += node.PhysicalProperties.mass;

            if (node.MainObject.GetComponent<BRigidBody>() != null)
                node.MainObject.AddComponent<Tracker>().Trace = true;
        }

        foreach (BRaycastRobot r in GetComponentsInChildren<BRaycastRobot>())
            r.RaycastRobot.EffectiveMass = collectiveMass;

        RotateRobot(robotStartOrientation);

        RobotName = new DirectoryInfo(directory).Name;

        isInitialized = true;

        bool hasRobotCamera = false;
        robotCameraManager = GameObject.Find("RobotCameraList").GetComponent<RobotCameraManager>();

        foreach (GameObject robotCamera in robotCameraManager.GetRobotCameraList())
        {
            if (robotCamera.GetComponent<RobotCamera>().robot.Equals(this))
            {
                robotCamera.GetComponent<RobotCamera>().RecoverConfiguration();
                hasRobotCamera = true;
                Debug.Log(hasRobotCamera);
            }

        }
        if (!hasRobotCamera)
        {
            //Attached to the main frame and face the front
            robotCameraManager.AddCamera(this, transform.GetChild(0).transform);
            //Attached to the first node and face the front
            if (transform.childCount > 1)
                robotCameraManager.AddCamera(this, transform.GetChild(1).transform);
            ////Attached to main frame and face the back
            robotCameraManager.AddCamera(this, transform.GetChild(0).transform, new Vector3(0, 0, 0), new Vector3(0, 180, 0));
        }

        //sensorManager.AddBeamBreaker(transform.GetChild(0).gameObject, new Vector3(0, 0, 1), new Vector3(0, 90, 0), 1);
        //sensorManager.AddUltrasonicSensor(transform.GetChild(0).gameObject, new Vector3(0, 0, 0), new Vector3(0, 0, 0));
        //sensorManager.AddGyro(transform.GetChild(0).gameObject, new Vector3(0, 0, 0), new Vector3(0, 0, 0));
        return true;
    }

    public void DeleteNodes()
    {
        //Deletes all nodes if any exist, take the old node transforms out from the robot object
        int childCount = manipulatorObject.transform.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            Transform child = manipulatorObject.transform.GetChild(i);

            //If not do this the game object is destroyed but the parent-child transform relationship remains!
            child.parent = null;
            Destroy(child.gameObject);
        }

        Destroy(manipulatorObject);
    }

     
    /// <summary>
    /// Return the robot to robotStartPosition and destroy extra game pieces
    /// </summary>
    /// <param name="resetTransform"></param>
    public void BeginReset()
    {
        IsResetting = true;
        foreach (Tracker t in UnityEngine.Object.FindObjectsOfType<Tracker>())
            t.Clear();

        foreach (RigidNode n in rootNode.ListAllNodes())
        {
            BRigidBody br = n.MainObject.GetComponent<BRigidBody>();

            if (br == null)
                continue;

            RigidBody r = (RigidBody)br.GetCollisionObject();

            r.LinearVelocity = r.AngularVelocity = BulletSharp.Math.Vector3.Zero;
            r.LinearFactor = r.AngularFactor = BulletSharp.Math.Vector3.Zero;

            BulletSharp.Math.Matrix newTransform = r.WorldTransform;
            newTransform.Origin = (robotStartPosition + n.ComOffset).ToBullet();
            newTransform.Basis = BulletSharp.Math.Matrix.Identity;
            r.WorldTransform = newTransform;
        }
        
        int hasManipulator = PlayerPrefs.GetInt("hasManipulator"); //0 is false, 1 is true
        int isMixAndMatch = PlayerPrefs.GetInt("MixAndMatch"); // 0 is false, 1 is true
        if (hasManipulator == 1 && isMixAndMatch == 1)
        {
            foreach (RigidNode n in manipulatorNode.ListAllNodes())
            {
                BRigidBody br = n.MainObject.GetComponent<BRigidBody>();

                if (br == null)
                    continue;

                RigidBody r = (RigidBody)br.GetCollisionObject();

                r.LinearVelocity = r.AngularVelocity = BulletSharp.Math.Vector3.Zero;
                r.LinearFactor = r.AngularFactor = BulletSharp.Math.Vector3.Zero;

                BulletSharp.Math.Matrix newTransform = r.WorldTransform;
                newTransform.Origin = (robotStartPosition + n.ComOffset).ToBullet();
                newTransform.Basis = BulletSharp.Math.Matrix.Identity;
                r.WorldTransform = newTransform;
            }

        }
        
        //Where "save orientation" works
        RotateRobot(robotStartOrientation);

        GameObject.Find("Robot").transform.GetChild(0).transform.position = new Vector3(10, 20, 5) ;
        if (IsResetting)
        {
            Debug.Log("is resetting!");
        }
    }

    /// <summary>
    /// Can move robot around in this state, update robotStartPosition if hit enter
    /// </summary>
    void Resetting()
    {
        if (Input.GetMouseButton(1))
        {
            //Transform rotation along the horizontal plane
            Vector3 rotation = new Vector3(0f,
                Input.GetKey(KeyCode.RightArrow) ? ResetVelocity : Input.GetKey(KeyCode.LeftArrow) ? -ResetVelocity : 0f,
                0f);
            if (!rotation.Equals(Vector3.zero))
                RotateRobot(rotation);

        }
        else
        {
            //Transform position
            Vector3 transposition = new Vector3(
                Input.GetKey(KeyCode.RightArrow) ? ResetVelocity : Input.GetKey(KeyCode.LeftArrow) ? -ResetVelocity : 0f,
                0f,
                Input.GetKey(KeyCode.UpArrow) ? ResetVelocity : Input.GetKey(KeyCode.DownArrow) ? -ResetVelocity : 0f);

            if (!transposition.Equals(Vector3.zero))
                TransposeRobot(transposition);
        }

        //Update robotStartPosition when hit enter
        if (Input.GetKey(KeyCode.Return))
        {
            robotStartOrientation = ((RigidNode)rootNode.ListAllNodes()[0]).MainObject.GetComponent<BRigidBody>().GetCollisionObject().WorldTransform.Basis;
            robotStartPosition = transform.GetChild(0).transform.position - nodeToRobotOffset;
            //Debug.Log(robotStartPosition);
            EndReset();
        }
    }

    /// <summary>
    /// End the reset process and puts the robot back down
    /// </summary>
    public void EndReset()
    {
        IsResetting = false;
        isResettingOrientation = false;

        foreach (RigidNode n in rootNode.ListAllNodes())
        {
            BRigidBody br = n.MainObject.GetComponent<BRigidBody>();

            if (br == null)
                continue;

            RigidBody r = (RigidBody)br.GetCollisionObject();

            r.LinearFactor = r.AngularFactor = BulletSharp.Math.Vector3.One;
        }

        int hasManipulator = PlayerPrefs.GetInt("hasManipulator"); //0 is false, 1 is true
        int isMixAndMatch = PlayerPrefs.GetInt("MixAndMatch"); // 0 is false, 1 is true
        if (hasManipulator == 1 && isMixAndMatch == 1)
        {
            foreach (RigidNode n in manipulatorNode.ListAllNodes())
            {
                BRigidBody br = n.MainObject.GetComponent<BRigidBody>();

                if (br == null)
                    continue;

                RigidBody r = (RigidBody)br.GetCollisionObject();

                r.LinearFactor = r.AngularFactor = BulletSharp.Math.Vector3.One;
            }
        }

    }

    /// <summary>
    /// Shifts the robot by a set position vector
    /// </summary>
    public void TransposeRobot(Vector3 transposition)
    {
        foreach (RigidNode n in rootNode.ListAllNodes())
        {
            BRigidBody br = n.MainObject.GetComponent<BRigidBody>();

            if (br == null)
                continue;

            RigidBody r = (RigidBody)br.GetCollisionObject();

            BulletSharp.Math.Matrix newTransform = r.WorldTransform;
            newTransform.Origin += transposition.ToBullet();
            r.WorldTransform = newTransform;
        }
    }

    /// <summary>
    /// Rotates the robot about its origin by a mathematical 4x4 matrix
    /// </summary>
    public void RotateRobot(BulletSharp.Math.Matrix rotationMatrix)
    {
        BulletSharp.Math.Vector3? origin = null;

        foreach (RigidNode n in rootNode.ListAllNodes())
        {
            BRigidBody br = n.MainObject.GetComponent<BRigidBody>();

            if (br == null)
                continue;

            RigidBody r = (RigidBody)br.GetCollisionObject();

            if (origin == null)
                origin = r.CenterOfMassPosition;

            BulletSharp.Math.Matrix rotationTransform = new BulletSharp.Math.Matrix();
            rotationTransform.Basis = rotationMatrix;
            rotationTransform.Origin = BulletSharp.Math.Vector3.Zero;

            BulletSharp.Math.Matrix currentTransform = r.WorldTransform;
            BulletSharp.Math.Vector3 pos = currentTransform.Origin;
            currentTransform.Origin -= origin.Value;
            currentTransform *= rotationTransform;
            currentTransform.Origin += origin.Value;

            r.WorldTransform = currentTransform;
        }
    }

    /// <summary>
    /// Rotates the robot about its origin by a set vector
    /// </summary>
    public void RotateRobot(Vector3 rotation)
    {
        RotateRobot(BulletSharp.Math.Matrix.RotationYawPitchRoll(rotation.y, rotation.z, rotation.x));
    }

    /// <summary>
    /// Resets the robot orientation to how the CAD model was originally defined (should be standing upright and facing forward if CAD was done properly)
    /// </summary>
    public void ResetRobotOrientation()
    {
        robotStartOrientation = BulletSharp.Math.Matrix.Identity;
        BeginReset();
        EndReset();
    }

    /// <summary>
    /// Saves the robot's current orientation to be used whenever robot is reset
    /// </summary>
    public void SaveRobotOrientation()
    {
        robotStartOrientation = ((RigidNode)rootNode.ListAllNodes()[0]).MainObject.GetComponent<BRigidBody>().GetCollisionObject().WorldTransform.Basis;
        robotStartOrientation.ToUnity();
        EndReset();
    }

    public DriverPracticeRobot GetDriverPractice()
    {
        return GetComponent<DriverPracticeRobot>();
    }

    public bool LoadManipulator(string directory)
    {
        manipulatorObject = new GameObject("Manipulator");

        //Set the manipulator transform to match with the position of node_0 of the robot. THIS ONE ACTUALLY DOES SOMETHING:
        //manipulatorObject.transform.position = GameObject.Find("Robot").transform.GetChild(0).transform.position;

        manipulatorObject.transform.position = gameObject.transform.GetChild(0).transform.position;
        //manipulatorObject.transform.position = robotStartPosition;

        RigidNode_Base.NODE_FACTORY = delegate (Guid guid)
        {
            return new RigidNode(guid);
        };

        List<RigidNode_Base> nodes = new List<RigidNode_Base>();
        //TO-DO: Read .robot instead (from the new exporters if they are implemented). Maybe need a RobotSkeleton class
        manipulatorNode = BXDJSkeleton.ReadSkeleton(directory + "\\skeleton.bxdj");
        manipulatorNode.ListAllNodes(nodes);

        int numWheels = nodes.Count(x => x.HasDriverMeta<WheelDriverMeta>() && x.GetDriverMeta<WheelDriverMeta>().type != WheelType.NOT_A_WHEEL);
        float collectiveMass = 0f;

        //Load node_0 for attaching manipulator to robot
        RigidNode node = (RigidNode)nodes[0];
        node.CreateTransform(manipulatorObject.transform);
        if (!node.CreateMesh(directory + "\\" + node.ModelFileName))
        {
            Debug.Log("Robot not loaded!");
            UnityEngine.Object.Destroy(manipulatorObject);
            return false;
        }
        node.CreateManipulatorJoint();
        node.MainObject.AddComponent<Tracker>().Trace = true;
        Tracker t = node.MainObject.GetComponent<Tracker>();
        Debug.Log(t);

        //Load other nodes associated with the manipulator
        for (int i = 1; i < nodes.Count; i++)
        {
            RigidNode otherNode = (RigidNode)nodes[i];
            otherNode.CreateTransform(manipulatorObject.transform);
            if (!otherNode.CreateMesh(directory + "\\" + otherNode.ModelFileName))
            {
                Debug.Log("Robot not loaded!");
                UnityEngine.Object.Destroy(manipulatorObject);
                return false;
            }
            otherNode.CreateJoint(numWheels);
            otherNode.MainObject.AddComponent<Tracker>().Trace = true;
            t = otherNode.MainObject.GetComponent<Tracker>();
            Debug.Log(t);
        }

        foreach (BRaycastRobot r in manipulatorObject.GetComponentsInChildren<BRaycastRobot>())
            r.RaycastRobot.EffectiveMass = collectiveMass;

        RotateRobot(robotStartOrientation);
        return true;
    }
}