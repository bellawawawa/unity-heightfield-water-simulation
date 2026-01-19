using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ensures MeshFilter and MeshRenderer are present so the mesh is visible in the editor
[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class WaterHeightmap : MonoBehaviour
{
    public int width = 100;
    public int height = 100; // Number of vertices along the X and Z axises
    public float scale = 1f; // Vertical scale of the water surface
    public float initialHeight = 0f; // Initial height of the water surface
    private MeshFilter meshFilter; // Reference to the MeshFilter component
    private Mesh mesh; // The mesh representing the water surface
    private float[,] heights; // 2D array storing the height of each vertex
    // Ripple parameters
    [Tooltip("Ripple strength as a fraction of mesh height (0.01 = 1% of mesh height)")]
    public float rippleStrength = 0.02f;
    [Tooltip("Ripple radius as a fraction of mesh width (0.05 = 5% of mesh width)")]
    public float rippleRadius = 0.05f;
    public float rippleDamping = 0.95f;
    private float[,] velocities;
    [Tooltip("Controls how fast waves propagate (lower = slower, e.g. 1.0 or 0.5)")]
    public float waveSpeed = 70f;


    // Called when the script is loaded or a value is changed in the Inspector
    void Awake()
    {
        GenerateAndAssignMesh();
        velocities = new float[width, height];
    }

    // Called in the editor when a value is changed in the Inspector
    void OnValidate()
    {
        GenerateAndAssignMesh();
        velocities = new float[width, height];
    }

    // For mouse drag simulation
    private bool isDragging = false;
    private Vector2? lastDragUV = null;

    void Update()
    {
        if (mesh == null || heights == null) return;

        // Handle mouse press and drag for ripple/drag effect
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            lastDragUV = null;
        }
        if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            lastDragUV = null;
        }

        if (isDragging)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector3 local = transform.InverseTransformPoint(hit.point);
                float fx = Mathf.Clamp(local.x, 0, width - 1);
                float fy = Mathf.Clamp(local.z, 0, height - 1);
                float u = fx / (width - 1);
                float v = fy / (height - 1);
                Vector2 curr = new Vector2(u, v);
                if (lastDragUV.HasValue)
                {
                    Vector2 prev = lastDragUV.Value;
                    float dist = Vector2.Distance(prev, curr);
                    if (dist > 0.0001f) // Only apply if mouse moved
                    {
                        int steps = Mathf.CeilToInt(dist * Mathf.Max(width, height) * 2f);
                        for (int i = 1; i <= steps; i++)
                        {
                            float t = i / (float)steps;
                            Vector2 lerp = Vector2.Lerp(prev, curr, t);
                            // Scale force by drag speed (distance moved)
                            AddRippleNormalized(lerp.x, lerp.y, dist * 2f);
                        }
                    }
                }
                else
                {
                    AddRippleNormalized(u, v);
                }
                lastDragUV = curr;
            }
        }

        // Simple ripple simulation: propagate and dampen (scaled by waveSpeed and Time.deltaTime)
        float dt = Application.isPlaying ? Time.deltaTime : 1f; // Use 1 in editor, deltaTime in play mode
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float avg = (
                    heights[x - 1, y] + heights[x + 1, y] +
                    heights[x, y - 1] + heights[x, y + 1]) * 0.25f;
                float force = avg - heights[x, y];
                velocities[x, y] = (velocities[x, y] + force * waveSpeed * dt) * Mathf.Pow(rippleDamping, dt * 60f);
            }
        }
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                heights[x, y] += velocities[x, y] * waveSpeed * dt;
            }
        }

        // Update mesh vertices
        Vector3[] vertices = mesh.vertices;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = heights[x, y] * scale;
                vertices[y * width + x] = new Vector3(x, h, y);
            }
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
    }

    // Adds a ripple at the given grid coordinate
    // Adds a ripple at a normalized (0-1) mesh position
    [Tooltip("Maximum force multiplier for drag splashes (prevents unrealistic splashes when dragging very fast)")]
    public float maxDragSplashForce = 2.5f;

    void AddRippleNormalized(float normX, float normY, float forceScale = 1f)
    {
        // Clamp the force scale to prevent unrealistic splashes
        float clampedForce = Mathf.Min(forceScale, maxDragSplashForce);
        int cx = Mathf.RoundToInt(normX * (width - 1));
        int cy = Mathf.RoundToInt(normY * (height - 1));
        float absRadius = Mathf.Max(2, Mathf.RoundToInt(rippleRadius * width));
        float absStrength = rippleStrength * height * clampedForce;
        Debug.Log($"Adding ripple at mesh coordinates: x={cx}, y={cy}, absRadius={absRadius}, absStrength={absStrength}");
        for (int dy = -Mathf.CeilToInt(absRadius); dy <= Mathf.CeilToInt(absRadius); dy++)
        {
            for (int dx = -Mathf.CeilToInt(absRadius); dx <= Mathf.CeilToInt(absRadius); dx++)
            {
                int px = cx + dx;
                int py = cy + dy;
                if (px > 0 && px < width - 1 && py > 0 && py < height - 1)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= absRadius)
                    {
                        float falloff = 1f - (dist / absRadius);
                        velocities[px, py] += absStrength * falloff;
                    }
                }
            }
        }
    }
    // Generates the mesh and assigns it to the MeshFilter
    void GenerateAndAssignMesh()
    {
        meshFilter = GetComponent<MeshFilter>();
        // Initialize the heights array to the initial height
        heights = new float[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                heights[x, y] = initialHeight;
        // Generate the mesh and assign it
        mesh = GenerateMesh();
        meshFilter.mesh = mesh;
    }

    // Generates a flat mesh based on the heights array
    Mesh GenerateMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[width * height];
        int[] triangles = new int[(width - 1) * (height - 1) * 6];

        // Create vertices for each grid point
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = heights[x, y] * scale; // Height at this vertex
                vertices[y * width + x] = new Vector3(x, h, y);
            }
        }

        // Create triangles for each quad in the grid
        int t = 0;
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int i = y * width + x;
                // First triangle of the quad
                triangles[t++] = i;
                triangles[t++] = i + width;
                triangles[t++] = i + 1;

                // Second triangle of the quad
                triangles[t++] = i + 1;
                triangles[t++] = i + width;
                triangles[t++] = i + width + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals(); // For correct lighting
        return mesh;
    }
}
