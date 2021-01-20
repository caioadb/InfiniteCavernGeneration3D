using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChunkManager : MonoBehaviour
{
    const float viewerMoveThresholdForChunkUpdate = 10f;    // distance viewer needs to move to call UpdateVisibleChunks()
    const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;

    NoiseSettings settings;

    public Transform viewer;    // viewers transform

    Vector3 viewerPosition;
    Vector3 viewerPositionOld;
    float meshWorldSize;
    int chunksVisibleInViewDst = 2; // amount of chunks visible at any time ( current chunk + this in every direction)
                                // 0 makes only the current chunk visible, 1 makes all 9 currently surrounding chunks visible
                                // causes lag

    [SerializeField]
    GameObject chunk = null; // prefab of a terrain chunk

    Dictionary<Vector3, CreateChunk> terrainChunkDictionary = new Dictionary<Vector3, CreateChunk>();   // dictionary that keeps all terrain chunks created
    List<CreateChunk> visibleTerrainChunks = new List<CreateChunk>();   // list of all currently visible terrain chunks

    private void Start()
    {
        settings = new NoiseSettings();
        if (settings.seed == -1)    // if seed is -1, randomize seed
        {
            settings.seed = Random.Range(1, 10000);
        }
        meshWorldSize = settings.size;

        UpdateVisibleChunks();  // spawns first chunk

    }

    private void Update()
    {
        viewerPosition = new Vector3(viewer.position.x, viewer.position.y, viewer.position.z);

        if ((viewerPositionOld - viewerPosition).sqrMagnitude > sqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        }

    }

    // Changes visible status of chunks,
    // Or creates new chunks
    private void UpdateVisibleChunks()
    {
        HashSet<Vector3> alreadyUpdatedChunkCoords = new HashSet<Vector3>();

        for (int i = visibleTerrainChunks.Count - 1; i >= 0; i--)
        {
            alreadyUpdatedChunkCoords.Add(visibleTerrainChunks[i].coord);
            visibleTerrainChunks[i].UpdateTerrainChunk();
        }

        // checking which chunk the player is currently in
        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / meshWorldSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / meshWorldSize);
        int currentChunkCoordZ = Mathf.RoundToInt(viewerPosition.z / meshWorldSize);

        int j = 0;

        for (int xOffset = -chunksVisibleInViewDst; xOffset <= chunksVisibleInViewDst; xOffset++)
        {
            for (int yOffset = -j; yOffset <= j; yOffset++)
            {
                for (int zOffset = -j; zOffset <= j; zOffset++)
                {
                    Vector3 viewedChunkCoord = new Vector3(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset, currentChunkCoordZ + zOffset);
                    if (!alreadyUpdatedChunkCoords.Contains(viewedChunkCoord))
                    {
                        if (terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                        {
                            terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                        }
                        else
                        {
                            CreateChunk newChunk = Instantiate(chunk, viewedChunkCoord, Quaternion.identity, transform).GetComponent<CreateChunk>();
                            newChunk.CreateChunkValues(viewedChunkCoord, transform, viewer, settings);
                            newChunk.UpdateTerrainChunk();
                            terrainChunkDictionary.Add(viewedChunkCoord, newChunk);
                            newChunk.onVisibilityChanged += OnTerrainChunkOnVisibilityChanged;
                        }
                    }
                }
            }
            if (xOffset < 0)
                j++;
            else if (xOffset >= 0)
                j--;
        }
    }

    // Adds or removes chunks from Visible list
    private void OnTerrainChunkOnVisibilityChanged(CreateChunk chunk, bool isVisible)
    {
        if (isVisible)
        {
            visibleTerrainChunks.Add(chunk);
        }
        else
        {
            visibleTerrainChunks.Remove(chunk);
        }
    }

}

public class NoiseSettings
{
    [Range(1, 32)]
    public int size = 20;

    public float scale = 20f;

    public int seed = -1;
    public Vector3 offset;

    public void ValidateValues()
    {
        scale = Mathf.Max(scale, 0.01f);

        size = Mathf.Clamp(size, 1, 32);
        seed = Mathf.Max(seed, -1);
    }
}