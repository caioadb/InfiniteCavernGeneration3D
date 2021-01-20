using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Noise;


// Assistant class to move information from secondary thread to main
public class MeshSettings
{
    public List<Vector3> vertices;
    public List<int> triangles;
    public List<Vector2> uv;

    public MeshSettings()
    {
        vertices = new List<Vector3>();
        triangles = new List<int>();
        uv = new List<Vector2>();
    }
}

// Assistant class to keep the points and their information in a grid
public class GridPoint
{
    private Vector3 _position = Vector3.zero;
    private bool _on = false;

    public Vector3 Position
    {
        get
        {
            return _position;
        }
        set
        {
            _position = new Vector3(value.x, value.y, value.z);
        }
    }
    public bool On
    {
        get
        {
            return _on;
        }
        set
        {
            _on = value;
        }
    }
    public override string ToString()
    {
        return string.Format("{0} {1}", Position, On);
    }
}

public class CreateChunk : MonoBehaviour
{
    OpenSimplex2S noise;    //noise function

    GameObject meshObject;
    MeshFilter meshFilter;
    Mesh mesh;

    public Material material = null; 
    public event System.Action<CreateChunk, bool> onVisibilityChanged;
    bool onValuesCreated;

    private List<Vector3> vertices;
    private List<int> triangles;
    private List<Vector2> uv;

    public Vector3 coord;
    Vector3 sampleCenter;
    Bounds bounds;
    Transform viewer;

    NoiseSettings settings;

    public int size;//x,y,z sizes of chunk

    Vector3 pointC; // Central point used in creation of vertices

    GridPoint[,,] points = null;    // Grid (x,y,z) of points

    MeshSettings meshSettings;

    Vector3 viewerPositionOld;
    const float viewerMoveThresholdForChunkUpdate = 10f;
    const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;


    // Creates and assigns values to variables
    public void CreateChunkValues(Vector3 coord, Transform parent, Transform viewer, NoiseSettings settings)
    {
        this.settings = new NoiseSettings();
        this.settings = settings;
        this.viewer = viewer;
        this.coord = coord;
        this.size = settings.size + 2;

        points = new GridPoint[size, size, size];//array of points that forms the chunk grid
        vertices = new List<Vector3>();//vertices that form terrain
        triangles = new List<int>();//triangles that connect vertices
        uv = new List<Vector2>();

        sampleCenter = coord * settings.size;//center of a terrain chunk
        Vector3 position = coord * (settings.size);//global position of terrain chunk

        meshObject = this.gameObject;
        meshFilter = meshObject.AddComponent<MeshFilter>();

        meshObject.transform.position = new Vector3(position.x, position.y, position.z);
        meshObject.transform.parent = parent;
        
        bounds = new Bounds(position, Vector3.one * this.size);
        SetVisible(false);

        onValuesCreated = true;
    }

    private void Update()
    {
        // Create and fill chunk grid
        if (onValuesCreated)
        {
            meshSettings = new MeshSettings();
            onValuesCreated = false;
            noise = new OpenSimplex2S(settings.seed);

            mesh = meshFilter.mesh;

            CreateGrid();

            GridToMeshThreaded();

        }

        // Updates visibility of terrain chunks
        if ((viewerPositionOld - viewerPosition).sqrMagnitude > sqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld = viewerPosition;
            UpdateTerrainChunk();
        }
    }

    // Calls CheckGrid in another thread to make performance better
    public void GridToMeshThreaded()
    {
        ThreadedDataRequester.RequestData(() => CheckGrid(), OnGridMade);
    }

    // When CheckGrid is done, assign values vertices, triangles and uvs,
    // and calls CreateMesh
    private void OnGridMade(object meshObject)
    {
        this.meshSettings = (MeshSettings)meshObject;

        vertices = meshSettings.vertices;
        triangles = meshSettings.triangles;
        uv = meshSettings.uv;

        CreateMesh();
    }

    // Assigns noise values to each point in the grid
    private void CreateGrid()
    {

        float halfSize = (float)size / 2f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {

                    float valueX = (x - halfSize + sampleCenter.x) / settings.scale;
                    float valueY = (y - halfSize + sampleCenter.y) / settings.scale;
                    float valueZ = (z - halfSize + sampleCenter.z) / settings.scale;

                    points[x, y, z] = new GridPoint();
                    points[x, y, z].Position = new Vector3(x, y, z);

                    points[x, y, z].On = (noise.Noise3_XZBeforeY(valueX, valueY, valueZ) > 0f);
                    
                }
            }
        }
    }

    // Applies triangles, vertices and uvs to mesh
    private void CreateMesh()
    {
        mesh.Clear();

        GameObject go = this.gameObject;
        MeshRenderer mr = go.AddComponent<MeshRenderer>(); //add meshrenderer component
        go.GetComponent<MeshCollider>().sharedMesh = mesh;

        mr.material = material;

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uv.ToArray();
        //Debug.Log(mesh.vertexCount);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // Checks points in the grid to create triangles
    private MeshSettings CheckGrid()
    {
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (points[x, y, z].Position.x == size-1 || points[x, y, z].Position.y == size-1 || points[x, y, z].Position.z == size-1)
                    {
                        continue;
                    }
                    CheckCube(points[x, y, z].Position);

                }
            }
        }

        MeshSettings meshTemp = new MeshSettings();
        meshTemp.vertices = vertices;
        meshTemp.triangles = triangles;
        meshTemp.uv = uv;

        return meshTemp;
    }

    private void CheckCube(Vector3 point)
    {
        int x = (int)point.x;
        int y = (int)point.y;
        int z = (int)point.z;

        int cubeConfig = 0;

        if (points[x, y + 1, z].On) { cubeConfig += 1; }
        if (points[x + 1, y + 1, z].On) { cubeConfig += 2; }
        if (points[x, y, z].On) { cubeConfig += 4; }
        if (points[x+1, y, z].On) { cubeConfig += 8; }
        if (points[x, y+1, z + 1].On) { cubeConfig += 16; }
        if (points[x+1, y+1, z + 1].On) { cubeConfig += 32; }
        if (points[x, y, z + 1].On) { cubeConfig += 64; }
        if (points[x+1, y, z + 1].On) { cubeConfig += 128; }

        pointC = points[x, y, z].Position;


        IsoFaces(cubeConfig);
    }

    // Changes visibility of chunk
    public void SetVisible(bool visible)
    {
        meshObject.SetActive(visible);
    }

    // Returns current visibility of chunk
    public bool IsVisible()
    {
        return meshObject.activeSelf;
    }

    // Updates visibility of chunk
    public void UpdateTerrainChunk()
    {
        float viewerDstFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));

        bool wasVisible = IsVisible();
        bool visible = viewerDstFromNearestEdge <= 6 * size;

        if (wasVisible != visible)
        {
            SetVisible(visible);
            if (onVisibilityChanged != null)
            {
                onVisibilityChanged(this, visible);
            }
        }

    }

    // Get method for current viewer position
    private Vector3 viewerPosition
    {
        get
        {
            return new Vector3(viewer.position.x, viewer.position.y, viewer.position.z);
        }
    }

    private int IsoFaces(int config)
    {
        // IMPORTANT GOLDEN INFORMATION!!! - the mesh triangles to make for each cube configuration

        // SANITY CHECK RULES:
        //  1. on cube corners are considered the inside of a mesh 2^8 = 256 
        //  2. connect triangle points in clockwise direction, on corners inside the mesh
        //  3. on off corners should always be separated by triangle corner
        //  4. same side corners should not have separation by triangle corner

        //vertices
        int beforeCount = vertices.Count;
        switch (config)
        {
            case 0:     // --------
                break;
            case 1:     // -------A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 2:     // ------B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 3:     // ------BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 4:     // -----C--
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 5:     // -----C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 6:     // -----CB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 7:     // -----CBA                
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 8:     // ----D---
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 9:     // ----D--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 10:    // ----D-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 11:    // ----D-BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 12:    // ----DC--
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 13:    // ----DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 14:    // ----DCB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 15:    // ----DCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 16:    // ---E----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 17:    // ---E---A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                break;
            case 18:    // ---E--B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 19:    // ---E--BA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 20:    // ---E-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 21:    // ---E-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 22:    // ---E-CB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 23:    // ---E-CBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 24:    // ---ED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 25:    // ---ED--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 26:    // ---ED-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 27:    // ---ED-BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 28:    // ---EDC--
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 29:    // ---EDC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 30:    // ---EDCB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 31:    // ---EDCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 32:    // --F-----
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 33:    // --F----A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 34:    // --F---B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 35:    // --F---BA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 36:    // --F--C--
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 37:    // --F--C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 38:    // --F--CB-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 39:    // --F--CBA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 40:    // --F-D---
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 41:    // --F-D--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 42:    // --F-D-B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 43:    // --F-D-BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 44:    // --F-DC--
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 45:    // --F-DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 46:    // --F-DCB-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                break;
            case 47:    // --F-DCBA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                break;
            case 48:    // --FE----
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 49:    // --FE---A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 50:    // --FE--B-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 51:    // --FE--BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 52:    // --FE-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 53:    // --FE-C-A
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 54:    // --FE-CB-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 55:    // --FE-CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 56:    // --FED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 57:    // --FED--A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 58:    // --FED-B-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 59:    // --FED-BA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 60:    // --FEDC--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 61:    // --FEDC-A
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 62:    // --FEDCB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 63:    // --FEDCBA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 64:    // -G------
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 65:    // -G-----A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 66:    // -G----B-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 67:    // -G----BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 68:    // -G---C--
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 69:    // -G---C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 70:    // -G---CB-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 71:    // -G---CBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 72:    // -G--D---
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 73:    // -G--D--A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 74:    // -G--D-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 75:    // -G--D-BA
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 76:    // -G--DC--
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 77:    // -G--DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 78:    // -G--DCB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 79:    // -G--DCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 80:    // -G-E----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 81:    // -G-E---A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 82:    // -G-E--B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                break;
            case 83:    // -G-E--BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 84:    // -G-E-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 85:    // -G-E-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 86:    // -G-E-CB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 87:    // -G-E-CBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 88:    // -G-ED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 89:    // -G-ED--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 90:    // -G-ED-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 91:    // -G-ED-BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 92:    // -G-EDC--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 93:    // -G-EDC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 94:    // -G-EDCB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 95:    // -G-EDCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 96:    // -GF-----
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 97:    // -GF----A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 98:    // -GF---B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 99:    // -GF---BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 100:   // -GF--C--
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 101:   // -GF--C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 102:   // -GF--CB-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 103:   // -GF--CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 104:   // -GF-D---
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 105:   // -GF-D--A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 106:   // -GF-D-B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 107:   // -GF-D-BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 108:   // -GF-DC--
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 109:   // -GF-DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 110:   // -GF-DCB-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 111:   // -GF-DCBA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 112:   // -GFE----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 113:   // -GFE---A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 114:   // -GFE--B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 115:   // -GFE--BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 116:   // -GFE-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 117:   // -GFE-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 118:   // -GFE-CB-
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 119:   // -GFE-CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 120:   // -GFED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 121:   // -GFED--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 122:   // -GFED-B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 123:   // -GFED-BA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 124:   // -GFEDC--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 125:   // -GFEDC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 126:   // -GFEDCB-
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 127:   // -GFEDCBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 128:   // H-------
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 129:   // H------A
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 130:   // H-----B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 131:   // H-----BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 132:   // H----C--
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 133:   // H----C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 134:   // H----CB-
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 135:   // H----CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 136:   // H---D---
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 137:   // H---D--A
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 138:   // H---D-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 139:   // H---D-BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 140:   // H---DC--
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 141:   // H---DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 142:   // H---DCB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 143:   // H---DCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 144:   // H--E----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 145:   // H--E---A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 146:   // H--E--B-
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 147:   // H--E--BA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 148:   // H--E-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 149:   // H--E-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 150:   // H--E-CB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));

                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));

                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 151:   // H--E-CBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 152:   // H--ED---
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 153:   // H--ED--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 154:   // H--ED-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 155:   // H--ED-BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 156:   // H--EDC--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 157:   // H--EDC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 158:   // H--EDCB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 159:   // H--EDCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 160:   // H-F-----
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 161:   // H-F----A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 162:   // H-F---B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 163:   // H-F---BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 164:   // H-F--C--
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 165:   // H-F--C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 166:   // H-F--CB-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 167:   // H-F--CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 168:   // H-F-D---
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 169:   // H-F-D--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 170:   // H-F-D-B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 171:   // H-F-D-BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 172:   // H-F-DC--
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 173:   // H-F-DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 174:   // H-F-DCB-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 175:   // H-F-DCBA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 176:   // H-FE----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 177:   // H-FE---A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 178:   // H-FE--B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 179:   // H-FE--BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 180:   // H-FE-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 181:   // H-FE-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 182:   // H-FE-CB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 183:   // H-FE-CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));

                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));

                break;
            case 184:   // H-FED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 185:   // H-FED--A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 186:   // H-FED-B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 187:   // H-FED-BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 188:   // H-FEDC--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 189:   // H-FEDC-A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 190:   // H-FEDCB-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 191:   // H-FEDCBA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 1f));
                break;
            case 192:   // HG------
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 193:   // HG-----A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 194:   // HG----B-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 195:   // HG----BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 196:   // HG---C--
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 197:   // HG---C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 198:   // HG---CB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 199:   // HG---CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 200:   // HG--D---
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 201:   // HG--D--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 202:   // HG--D-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 203:   // HG--D-BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 204:   // HG--DC--
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 205:   // HG--DC-A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 206:   // HG--DCB-
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 207:   // HG--DCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 208:   // HG-E----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 209:   // HG-E---A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 210:   // HG-E--B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 211:   // HG-E--BA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 212:   // HG-E-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 213:   // HG-E-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 214:   // HG-E-CB-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 215:   // HG-E-CBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 216:   // HG-ED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 217:   // HG-ED--A
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 218:   // HG-ED-B-
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 219:   // HG-ED-BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 220:   // HG-EDC--
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 221:   // HG-EDC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 222:   // HG-EDCB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 223:   // HG-EDCBA
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 1f));
                break;
            case 224:   // HGF-----
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                break;
            case 225:   // HGF----A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                break;
            case 226:   // HGF---B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 227:   // HGF---BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 228:   // HGF--C--
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                break;
            case 229:   // HGF--C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 230:   // HGF--CB-
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 231:   // HGF--CBA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 232:   // HGF-D---
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 233:   // HGF-D--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 234:   // HGF-D-B-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 235:   // HGF-D-BA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 236:   // HGF-DC--
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 237:   // HGF-DC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                break;
            case 238:   // HGF-DCB-
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 239:   // HGF-DCBA
                vertices.Add(pointC + new Vector3(0.5f, 1f, 1f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 1f));
                break;
            case 240:   // HGFE----
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 241:   // HGFE---A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                break;
            case 242:   // HGFE--B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 243:   // HGFE--BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 244:   // HGFE-C--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 245:   // HGFE-C-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                break;
            case 246:   // HGFE-CB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 247:   // HGFE-CBA
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 248:   // HGFED---
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 249:   // HGFED--A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 250:   // HGFED-B-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                break;
            case 251:   // HGFED-BA
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0.5f, 0f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0f, 0.5f));
                break;
            case 252:   // HGFEDC--
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 253:   // HGFEDC-A
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(1f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(1f, 0.5f, 0f));
                break;
            case 254:   // HGFEDCB-
                vertices.Add(pointC + new Vector3(0f, 1f, 0.5f));
                vertices.Add(pointC + new Vector3(0.5f, 1f, 0f));
                vertices.Add(pointC + new Vector3(0f, 0.5f, 0f));
                break;
            case 255:   // HGFEDCBA
                break;
            default:
                Debug.LogError(string.Format("unhandled config encountered [config]=" + config));
                break;
        }
        int addedCount = vertices.Count - beforeCount;

        //triangles, uvs (verticies added count enables to add proper triangle, uvs array items)
        switch (addedCount)
        {
            case 0:
                break;
            case 3:     // 1 triangle
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 6:     // 2 triangle
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 9:     // 3 triangle
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 12:    // 4 triangle
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 15:    // 5 triangles
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 18:    // 6 triangles
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 21: // 7 triangles
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 24: // 8 triangles
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 27:    //9 triangles
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 30:    //10 triangles
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 33:    //11 triangles
                triangles.Add(vertices.Count - 33);
                triangles.Add(vertices.Count - 32);
                triangles.Add(vertices.Count - 31);
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 36:    //12 triangles
                triangles.Add(vertices.Count - 36);
                triangles.Add(vertices.Count - 35);
                triangles.Add(vertices.Count - 34);
                triangles.Add(vertices.Count - 33);
                triangles.Add(vertices.Count - 32);
                triangles.Add(vertices.Count - 31);
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 39:    //13 triangles
                triangles.Add(vertices.Count - 39);
                triangles.Add(vertices.Count - 38);
                triangles.Add(vertices.Count - 37);
                triangles.Add(vertices.Count - 36);
                triangles.Add(vertices.Count - 35);
                triangles.Add(vertices.Count - 34);
                triangles.Add(vertices.Count - 33);
                triangles.Add(vertices.Count - 32);
                triangles.Add(vertices.Count - 31);
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 42:    //14 triangles
                triangles.Add(vertices.Count - 42);
                triangles.Add(vertices.Count - 41);
                triangles.Add(vertices.Count - 40);
                triangles.Add(vertices.Count - 39);
                triangles.Add(vertices.Count - 38);
                triangles.Add(vertices.Count - 37);
                triangles.Add(vertices.Count - 36);
                triangles.Add(vertices.Count - 35);
                triangles.Add(vertices.Count - 34);
                triangles.Add(vertices.Count - 33);
                triangles.Add(vertices.Count - 32);
                triangles.Add(vertices.Count - 31);
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            case 45:    //15 triangles
                triangles.Add(vertices.Count - 45);
                triangles.Add(vertices.Count - 44);
                triangles.Add(vertices.Count - 43);
                triangles.Add(vertices.Count - 42);
                triangles.Add(vertices.Count - 41);
                triangles.Add(vertices.Count - 40);
                triangles.Add(vertices.Count - 39);
                triangles.Add(vertices.Count - 38);
                triangles.Add(vertices.Count - 37);
                triangles.Add(vertices.Count - 36);
                triangles.Add(vertices.Count - 35);
                triangles.Add(vertices.Count - 34);
                triangles.Add(vertices.Count - 33);
                triangles.Add(vertices.Count - 32);
                triangles.Add(vertices.Count - 31);
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                break;
            case 48:    //16 triangles
                triangles.Add(vertices.Count - 48);
                triangles.Add(vertices.Count - 47);
                triangles.Add(vertices.Count - 46);
                triangles.Add(vertices.Count - 45);
                triangles.Add(vertices.Count - 44);
                triangles.Add(vertices.Count - 43);
                triangles.Add(vertices.Count - 42);
                triangles.Add(vertices.Count - 41);
                triangles.Add(vertices.Count - 40);
                triangles.Add(vertices.Count - 39);
                triangles.Add(vertices.Count - 38);
                triangles.Add(vertices.Count - 37);
                triangles.Add(vertices.Count - 36);
                triangles.Add(vertices.Count - 35);
                triangles.Add(vertices.Count - 34);
                triangles.Add(vertices.Count - 33);
                triangles.Add(vertices.Count - 32);
                triangles.Add(vertices.Count - 31);
                triangles.Add(vertices.Count - 30);
                triangles.Add(vertices.Count - 29);
                triangles.Add(vertices.Count - 28);
                triangles.Add(vertices.Count - 27);
                triangles.Add(vertices.Count - 26);
                triangles.Add(vertices.Count - 25);
                triangles.Add(vertices.Count - 24);
                triangles.Add(vertices.Count - 23);
                triangles.Add(vertices.Count - 22);
                triangles.Add(vertices.Count - 21);
                triangles.Add(vertices.Count - 20);
                triangles.Add(vertices.Count - 19);
                triangles.Add(vertices.Count - 18);
                triangles.Add(vertices.Count - 17);
                triangles.Add(vertices.Count - 16);
                triangles.Add(vertices.Count - 15);
                triangles.Add(vertices.Count - 14);
                triangles.Add(vertices.Count - 13);
                triangles.Add(vertices.Count - 12);
                triangles.Add(vertices.Count - 11);
                triangles.Add(vertices.Count - 10);
                triangles.Add(vertices.Count - 9);
                triangles.Add(vertices.Count - 8);
                triangles.Add(vertices.Count - 7);
                triangles.Add(vertices.Count - 6);
                triangles.Add(vertices.Count - 5);
                triangles.Add(vertices.Count - 4);
                triangles.Add(vertices.Count - 3);
                triangles.Add(vertices.Count - 2);
                triangles.Add(vertices.Count - 1);
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));
                uv.Add(new Vector2(1, 0));
                break;
            default:
                Debug.LogError("unhandled addedCount encountered [addedCount]= " + addedCount);
                break;
        }

        return addedCount;
    }
}
