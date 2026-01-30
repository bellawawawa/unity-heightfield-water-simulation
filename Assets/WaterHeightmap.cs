using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ensures MeshFilter and MeshRenderer are present so the mesh is visible in the editor
[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
// Ensure there is a collider for raycasts to hit on mouseclick
[RequireComponent(typeof(MeshCollider))]
public class WaterHeightmap : MonoBehaviour
{
    // Number of vertices along the X and Z axises
    public int width = 100;
    public int height = 100; 
    [Tooltip("Vertical exaggeration of water surface, multiplies with height value of each vertex on ripple")] // scale
    public float scale = 3f;
    public float initialHeight = 0f; // Initial height of each vertex
    private MeshFilter meshFilter; // Reference to the MeshFilter component
    private Mesh mesh; // The mesh representing the water surface
    private float[,] heights; // 2D array storing the height of each vertex
    
    // Ripple parameters
    [Tooltip("Ripple strength as a fraction of mesh height (0.01 = 1% of mesh height)")]
    public float rippleStrength = 0.02f; // Initial strength of ripple
    [Tooltip("Ripple radius as a fraction of mesh width (0.05 = 5% of mesh width)")]
    public float rippleRadius = 0.05f; // Initial radius of splash
    public float rippleDamping = 0.95f; // How quickly ripples fade
    private float[,] velocities;
    [Tooltip("Controls how fast waves propagate (lower = slower, e.g. 1.0 or 0.5)")]
    public float rippleSpeed = 70f; // Speed of ripples
    [Tooltip("Maximum force multiplier for drag splashes (prevents unrealistic splashes when dragging very fast)")]
    public float maxDragSplashForce = 2.5f;

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
    private bool isDragging = false; // Are we moving the mouse?
    private Vector2? lastDragUV = null; // Position of previous drag event so we can track mouse movement

    void Update()
    {
        // --- Mouse Input ---
        // Handles mouse input for creating ripples on the water surface.
        // Tracks dragging starts and stops, adding ripples along the line.

        if (mesh == null || heights == null) return;

        // Handle mouse drag state based on raycast hit
        bool mouseHeld = Input.GetMouseButton(0);
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        bool hitWater = Physics.Raycast(ray, out RaycastHit hit);

        if (mouseHeld && hitWater)
        {
            if (!isDragging)
            {
                // Start a new drag if entering water while mouse is held
                isDragging = true;
                lastDragUV = null;
            }
        }
        else
        {
            // If mouse is not held or not over water, stop dragging
            isDragging = false;
            lastDragUV = null;
        }

        if (isDragging && hitWater)
        {
            // If it hits the mesh collider, store the hit location
            Vector3 local = transform.InverseTransformPoint(hit.point);
            float fx = Mathf.Clamp(local.x, 0, width - 1);
            float fy = Mathf.Clamp(local.z, 0, height - 1);
            float u = fx / (width - 1);
            float v = fy / (height - 1);
            Vector2 curr = new Vector2(u, v);
            // If there was a previous drag value (i.e., the mouse has moved)
            if (lastDragUV.HasValue)
            {
                // Ensure distance isn't negligable
                Vector2 prev = lastDragUV.Value;
                float dist = Vector2.Distance(prev, curr);
                if (dist > 0.0001f)
                {
                    // Scale number of points between previous and current mouse positions such that number of ripple points per unit of distance is consistent
                    // Also accounts for more dense meshes
                    // Prevents inconsistency between slower and faster mouse movements
                    int steps = Mathf.CeilToInt(dist * Mathf.Max(width, height) * 2f);
                    for (int i = 1; i <= steps; i++)
                    {
                        // For each ripple point, apply a ripple
                        float t = i / (float)steps;
                        Vector2 lerp = Vector2.Lerp(prev, curr, t);
                        // Reduced multiplier from 2f to 0.5f
                        AddRippleNormalized(lerp.x, lerp.y, dist * 0.5f);
                    }
                }
            }
            else
            {
                // If we aren't moving the mouse, just add a single ripple
                AddRippleNormalized(u, v);
            }
            lastDragUV = curr;
        }

        // --- Water Simulation ---
        // This section simulates the movement of water across the mesh.
        // It first calculates how each vertex (point) is affected by its neighbors,
        // updating the velocity to create wave motion. Then, it updates the height of each vertex
        // based on its velocity, making the waves move and spread. Finally, it applies these new heights
        // to the mesh so the water surface visually updates in the scene.

        float dt = Application.isPlaying ? Time.deltaTime : 1f; // Time step. Use 1 in editor, deltaTime in play mode.

        // Loop through every vertex except those on the edge
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                // Calculate average neight of its left, right, up and down neighbours
                float avg = (
                    heights[x - 1, y] + heights[x + 1, y] +
                    heights[x, y - 1] + heights[x, y + 1]) * 0.25f;
                // Find difference between this vertex's height and the average of its neighbours 
                float force = avg - heights[x, y];
                // Update velocity by adding force and then damping
                velocities[x, y] = (velocities[x, y] + force * rippleSpeed * dt) * Mathf.Pow(rippleDamping, dt * 60f);
            }
        }
        // Using a seperate loop to ensure stability
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                // Update height of each vertex by adding its velocity scaled by rippleSpeed and dt
                heights[x, y] += velocities[x, y] * rippleSpeed * dt;
            }
        }

        // Update mesh vertices
        Vector3[] vertices = mesh.vertices;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Apply calculated heights
                float h = heights[x, y] * scale;
                vertices[y * width + x] = new Vector3(x, h, y);
            }
        }
        // Assign all our caluclations to the actual mesh
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
    }

    // --- Add Ripples ---
    // Finds strength and radius of ripple
    // Goes through all nearby points, adding to their heights and falling off as 
    // we get further from the center
    void AddRippleNormalized(float normX, float normY, float forceScale = 1f)
    {
        // Clamp force to 1, preventing overly strong ripples
        float clampedForce = Mathf.Min(forceScale, 1.0f);
        // Convert normalized coordinates into integer grid indices on the mesh, getting the center of the ripple
        int cx = Mathf.RoundToInt(normX * (width - 1));
        int cy = Mathf.RoundToInt(normY * (height - 1));
        // Calculate ripple radius in grid units. Ensure a minimum of 2 so its always visible
        float absRadius = Mathf.Max(2, Mathf.RoundToInt(rippleRadius * width));
        // Calculate strength, clamp it
        float absStrength = rippleStrength * height * clampedForce * 0.5f; 
        absStrength = Mathf.Min(absStrength, 0.02f); 
        
        // For every vertex in a square region around the ripples centre point
        // (Offset from centre)
        for (int dy = -Mathf.CeilToInt(absRadius); dy <= Mathf.CeilToInt(absRadius); dy++)
        {
            for (int dx = -Mathf.CeilToInt(absRadius); dx <= Mathf.CeilToInt(absRadius); dx++)
            {
                // Find vertexe by adding offset to centre
                int px = cx + dx;
                int py = cy + dy;
                // Check we're inside the mesh and not on the edge
                if (px > 0 && px < width - 1 && py > 0 && py < height - 1)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy); // Distance from centre to vertex
                    if (dist <= absRadius) // If we're within the ripples radius
                    {
                        // Add strength to velocity
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
        // Update the MeshCollider to match the new mesh
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null; // Clear first to force update
            meshCollider.sharedMesh = mesh;
        }
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
