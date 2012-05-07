#define WATERMARK_OMERCY

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

// Note: This enum is identical to the OpenNI SkeletonJoint enum
// It doesn't have to be, but this ensures backwards compatibility
// with some of our recorded data (not to mention less conversion code ;))

public enum ZigJointId
{
 	None = 0,
 	Head,
 	Neck,
 	Torso,
 	Waist,
 	LeftCollar,
 	LeftShoulder,
 	LeftElbow,
 	LeftWrist,
 	LeftHand,
 	LeftFingertip,
 	RightCollar,
 	RightShoulder,
 	RightElbow,
 	RightWrist,
 	RightHand,
 	RightFingertip,
 	LeftHip,
 	LeftKnee,
 	LeftAnkle,
 	LeftFoot,
 	RightHip,
 	RightKnee,
 	RightAnkle,
 	RightFoot
}

public class ZigInputJoint
{
	public ZigJointId Id { get; private set; }
	public Vector3 Position;
	public Quaternion Rotation;
	public bool GoodPosition;
	public bool GoodRotation;
	
	public ZigInputJoint(ZigJointId id) :
		this(id, Vector3.zero, Quaternion.identity) {
            GoodPosition = false;
            GoodRotation = false;
    }
	
	public ZigInputJoint(ZigJointId id, Vector3 position, Quaternion rotation) {
		Id = id;
		Position = position;
		Rotation = rotation;
	}
}

public class ZigInputUser
{
	public int Id;
	public bool Tracked;
	public Vector3 CenterOfMass;
	public List<ZigInputJoint> SkeletonData;
	public ZigInputUser(int id, Vector3 com)
	{
		Id = id;
		CenterOfMass = com;
	}
}

public class NewUsersFrameEventArgs : EventArgs
{
	public NewUsersFrameEventArgs(List<ZigInputUser> users)
	{
		Users = users;
	}
	
	public List<ZigInputUser> Users { get; private set; }
}

public class ZigDepth {
    public int xres { get; private set; }
    public int yres { get; private set; }
    public short[] data;
    public ZigDepth(int x, int y) {
        xres = x;
        yres = y;
        data = new short[x * y];
    }
}

public class ZigImage
{
    public int xres { get; private set; }
    public int yres { get; private set; }
    public Color32[] data;
    public ZigImage(int x, int y) {
        xres = x;
        yres = y;
        data = new Color32[x * y];
    }
}

public class ZigLabelMap
{
    public int xres { get; private set; }
    public int yres { get; private set; }
    public short[] data;
    public ZigLabelMap(int x, int y)
    {
        xres = x;
        yres = y;
        data = new short[x * y];
    }
}

interface IZigInputReader
{
	// init/update/shutdown
	void Init();
	void Update();
	void Shutdown();
	
	// users & hands
	event EventHandler<NewUsersFrameEventArgs> NewUsersFrame;
	
    // streams
	bool UpdateDepth { get; set; }
	bool UpdateImage { get; set; }
    bool UpdateLabelMap { get; set; }

    ZigDepth Depth { get; }
    ZigImage Image { get; }
    ZigLabelMap LabelMap { get; }

    // misc
    Vector3 ConvertWorldToImageSpace(Vector3 worldPosition);
    Vector3 ConvertImageToWorldSpace(Vector3 imagePosition);
    bool AlignDepthToRGB { get; set; }
}

public class ZigTrackedUser
{
	List<GameObject> listeners = new List<GameObject>();

    public int Id { get; private set; }
    public bool PositionTracked { get; private set; }
    public Vector3 Position { get; private set; }
    public bool SkeletonTracked { get; private set; }
    public ZigInputJoint[] Skeleton { get; private set; }

	public ZigTrackedUser(ZigInputUser userData) {
        Skeleton = new ZigInputJoint[Enum.GetValues(typeof(ZigJointId)).Length];
        for (int i=0; i<Skeleton.Length; i++) {
            Skeleton[i] = new ZigInputJoint((ZigJointId)i);
        }
		Update(userData);
	}
		
	public void AddListener(GameObject listener) {
		listeners.Add(listener);
        listener.SendMessage("Zig_Attach", this, SendMessageOptions.DontRequireReceiver);
	}

    public void RemoveListener(GameObject listener) {
        if (null == listener) {
            listeners.Clear();
        }
        else {
            listeners.Remove(listener);
            listener.SendMessage("Zig_Detach", this, SendMessageOptions.DontRequireReceiver);
        }
    }
	
	public void Update(ZigInputUser userData) {
		Id = userData.Id;
        PositionTracked = true;
        Position = userData.CenterOfMass;
        SkeletonTracked = userData.Tracked;
        foreach (ZigInputJoint j in userData.SkeletonData) {
            Skeleton[(int)j.Id] = j;
        }
		notifyListeners("Zig_UpdateUser", this);
	}

    void notifyListeners(string msgname, object arg) {
        for (int i = 0; i < listeners.Count; ) {
            GameObject go = listeners[i];
            if (go) {
                go.SendMessage(msgname, arg, SendMessageOptions.DontRequireReceiver);
                i++;
            }
            else {
                listeners.RemoveAt(i);
            }
        }
    }
}

public enum ZigInputType {
    Auto,
	OpenNI,
	KinectSDK,
}


//-----------------------------------------------------------------------------
// ZigInput
//
// This Singleton/Monobehaviour makes sure we get input from a depth cam. The
// input can be from OpenNI, KinectSDK, or Webplayer.
//
// The Singleton part ensures only one instance that crosses scene boundries.
// The monobehaviour part ensures we get runtime via Update()
//
// A ZigInput component will be implicitly added to any scene that requires
// depth input. Add it explicitly to change paramters or switch the input
// type. The component will persist between scenes so make sure to only add it
// to the first scene that will use the sensor
//-----------------------------------------------------------------------------

public class ZigInput : MonoBehaviour {

	//-------------------------------------------------------------------------
	// Watermark stuff
	//-------------------------------------------------------------------------
	
	#if WATERMARK_OMERCY
	
	Texture2D watermarkTexture;
	
	Texture2D LoadTextureFromResource(string name) {
		// open resource stream
		Stream s = this.GetType().Assembly.GetManifestResourceStream(name);
		if (null == s) {
			return null;
		}
		
		// read & close
		byte[] data = new byte[s.Length];
		s.Read(data, 0, data.Length);
		s.Close();
		
		// load into texture
		Texture2D result = new Texture2D(1,1);
		result.LoadImage(data);
		return result;
	}
	
	void OnGUI() {
		GUI.DrawTexture(new Rect(10, Screen.height - 10 - watermarkTexture.height, watermarkTexture.width, watermarkTexture.height), watermarkTexture);
	}
	
	#endif
		
	//-------------------------------------------------------------------------
	// Singleton logic
	//-------------------------------------------------------------------------
		
	public static bool UpdateDepth;
	public static bool UpdateImage;
    public static bool UpdateLabelMap;

	public static ZigInputType InputType = ZigInputType.Auto;
	static ZigInput instance;
	public static ZigInput Instance
	{
		get {
			if (null == instance) {
                instance = FindObjectOfType(typeof(ZigInput)) as ZigInput;
                if (null == instance) {
                    GameObject container = new GameObject();
					DontDestroyOnLoad (container);
                    container.name = "ZigInputContainer";
                    instance = container.AddComponent<ZigInput>();
                }
				DontDestroyOnLoad(instance);
            }
			return instance;
		}
	}

    public static ZigDepth Depth { get; private set; }
    public static ZigImage Image { get; private set; }
    public static ZigLabelMap LabelMap { get; private set; }

    public bool AlignDepthToRGB = false;
	
	//-------------------------------------------------------------------------
	// MonoBehaviour logic
	//-------------------------------------------------------------------------
	
	public List<GameObject> listeners = new List<GameObject>();
    IZigInputReader reader;
	public bool ReaderInited { get; private set; }
	
	void Awake() {
		#if WATERMARK_OMERCY
		watermarkTexture = LoadTextureFromResource("ZDK.wm.png");
		#endif
		
		// reader factory
		if (Application.isWebPlayer) {
			reader = (new ZigInputWebplayer()) as IZigInputReader;
            ReaderInited = StartReader();
		} else {



            if (ZigInput.InputType == ZigInputType.Auto)
            {
                    print("Trying to open Kinect sensor using MS Kinect SDK");
                    reader = (new ZigInputKinectSDK()) as IZigInputReader;


                    if (StartReader())
                    {
                        ReaderInited = true; // KinectSDK
                    }
                    else
                    {
                        print("failed opening Kinect SDK sensor (if you're using Kinect SDK, please unplug the sensor, restart Unity and try again)");
                        print("Trying to open sensor using OpenNI");

                        reader = (new ZigInputOpenNI()) as IZigInputReader;
                        if (StartReader())
                        {
                            ReaderInited = true;
                        }
                        else
                        {
                            print("failed opening sensor using OpenNI");
                            Debug.LogError("Failed to load driver and middleware, review warnings above for specific exception messages from middleware");
                        }
                    }
            }
            else
            {
                if (ZigInput.InputType == ZigInputType.OpenNI)
                {
                    print("Trying to open sensor using OpenNI");
                    reader = (new ZigInputOpenNI()) as IZigInputReader;
                }
                else
                {
                    print("Trying to open Kinect sensor using MS Kinect SDK");
                    reader = (new ZigInputKinectSDK()) as IZigInputReader;
                }

                if (StartReader())
                {
                    ReaderInited = true; // KinectSDK
                }
                else
                {
                    Debug.LogError("Failed to load driver and middleware, consider setting the Zig Input Type to Auto");
                }

            }    
		}
	}

    private bool StartReader()
    {

        reader.NewUsersFrame += HandleReaderNewUsersFrame;
        reader.UpdateDepth = ZigInput.UpdateDepth;
        reader.UpdateImage = ZigInput.UpdateImage;
        reader.UpdateLabelMap = ZigInput.UpdateLabelMap;

        try {
            reader.Init();
            ZigInput.Depth = reader.Depth;
            ZigInput.Image = reader.Image;
            ZigInput.LabelMap = reader.LabelMap;
            return true;
        }
        catch (Exception ex) {
            Debug.LogWarning(ex.Message);
            return false;
        }
    }

    // Update is called once per frame
	void Update () {
		if (ReaderInited) {
			reader.Update();
		}
	}
	
	void OnApplicationQuit()
	{
		if (ReaderInited) {
			reader.Shutdown();	
			ReaderInited = false;
		}
	}
	
	public void AddListener(GameObject listener)
	{
		if (!listeners.Contains(listener)) {
			listeners.Add(listener);
		}

		foreach (ZigTrackedUser user in TrackedUsers.Values) {
            listener.SendMessage("Zig_UserFound", user, SendMessageOptions.DontRequireReceiver);
		}
	}

	
	Dictionary<int, ZigTrackedUser> trackedUsers = new Dictionary<int, ZigTrackedUser>();
	
	public Dictionary<int, ZigTrackedUser> TrackedUsers { 
		get {
			return trackedUsers;
		}
	}
	
	void HandleReaderNewUsersFrame(object sender, NewUsersFrameEventArgs e)
	{
		// get rid of old users
		List<int> idsToRemove = new List<int>(trackedUsers.Keys);
		foreach (ZigInputUser user in e.Users) {
			idsToRemove.Remove(user.Id);
		}
		foreach (int id in idsToRemove) {
			ZigTrackedUser user = trackedUsers[id];
			trackedUsers.Remove(id);
			notifyListeners("Zig_UserLost", user);
		}
			
		// add new & update existing users
		foreach (ZigInputUser user in e.Users) {
			if (!trackedUsers.ContainsKey(user.Id)) {
				ZigTrackedUser trackedUser = new ZigTrackedUser(user);
				trackedUsers.Add(user.Id, trackedUser);
                notifyListeners("Zig_UserFound", trackedUser);
			} else {
				trackedUsers[user.Id].Update(user);
			}
		}
		
		notifyListeners("Zig_Update", this);
	}
	
	void notifyListeners(string msgname, object arg)
	{
       for(int i = 0; i < listeners.Count; ) {
           GameObject go = listeners[i];
           if (go) {
               go.SendMessage(msgname, arg, SendMessageOptions.DontRequireReceiver);
               i++;
           }
           else {
               listeners.RemoveAt(i);
           }
		}
	}
    //-------------------------------------------------------------------------
    // World <-> Image space conversions
    //-------------------------------------------------------------------------
    public static Vector3 ConvertImageToWorldSpace(Vector3 imagePosition)
    {
        return Instance.reader.ConvertImageToWorldSpace(imagePosition);
    }

    public static Vector3 ConvertWorldToImageSpace(Vector3 worldPosition)
    {
        return Instance.reader.ConvertWorldToImageSpace(worldPosition);
    }
}
