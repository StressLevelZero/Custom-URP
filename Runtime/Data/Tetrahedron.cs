//using System.Collections;
using System.Collections.Generic;
// using System.Linq;
// using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

[System.Serializable]
public class Tetrahedron
    {
        public int Vert0,Vert1,Vert2,Vert3;       // Indices of the 4 vertices
        public int[] Neighbors;
     // public int Neighbor0,Neighbor1,Neighbor2,Neighbor3; // Indices of the 4 neighboring Tetrahedron
      public Matrix4x4 BaryMatrix;
     // public Matrix3x3 BaryMatrix; // Precomputed matrix for barycentric coordinate calculation //Using a 3x3 to remove extra data
     
     public Tetrahedron(int v0, int v1, int v2, int v3)
     {
         Vert0 = v0;
         Vert1 = v1;
         Vert2 = v2;
         Vert3 = v3;
         Neighbors = new int[] { -1, -1, -1, -1 };
     }
     
     // Checks if the tetrahedron contains a given vertex index
     public bool ContainsVertex(int vertexIndex)
     {
         return Vert0 == vertexIndex || Vert1 == vertexIndex || Vert2 == vertexIndex || Vert3 == vertexIndex;
     }
     public void ComputeBarycentricMatrix(Vector3[] vertices)
      {
          Vector3 v0 = vertices[Vert0];
          Vector3 v1 = vertices[Vert1];
          Vector3 v2 = vertices[Vert2];
          Vector3 v3 = vertices[Vert3];
          
          // / 4x4 method
          // Construct the matrix from the vertex positions
          Matrix4x4 m = new Matrix4x4(
              new Vector4(v0.x - v3.x, v0.y - v3.y, v0.z - v3.z,0),
              new Vector4(v1.x - v3.x, v1.y - v3.y, v1.z - v3.z,0),
              new Vector4(v2.x - v3.x, v2.y - v3.y, v2.z - v3.z,0),
              new Vector4(0,0,0,1)
              );
          
          // Precompute and store the inverse matrix
          BaryMatrix = Matrix4x4.Inverse(m);
          
          // / 3x3 method
          // Matrix3x3 m = new Matrix3x3(
          //    v0.x - v3.x, v0.y - v3.y, v0.z - v3.z,
          //    v1.x - v3.x, v1.y - v3.y, v1.z - v3.z,
          //     v2.x - v3.x, v2.y - v3.y, v2.z - v3.z
          // );
          //
          // // Precompute and store the inverse matrix
          // BaryMatrix = m.Inverse();
      }
        
        // Helper method to get neighbor by index
        // public int GetNeighborByIndex(int index)
        // {
        //     switch (index)
        //     {
        //         case 0:
        //             return Neighbor0;
        //         case 1:
        //             return Neighbor1;
        //         case 2:
        //             return Neighbor2;
        //         case 3:
        //             return Neighbor3;
        //         default:
        //             return -1;
        //     }
        // }
        
        // Function to convert a list of indices into a list of Tetrahedron objects
        public static Tetrahedron[] ConvertToTetrahedrons(int[] indices)
        {
            List<Tetrahedron> tetrahedrons = new List<Tetrahedron>();

            // Ensure the list of indices can be divided into groups of 4
            if (indices.Length % 4 != 0)
            {
                UnityEngine.Debug.LogError("The number of indices is not divisible by 4.");
                return tetrahedrons.ToArray();
            }

            // Loop through the indices and create Tetrahedron objects
            for (int i = 0; i < indices.Length; i += 4)
            {
                Tetrahedron tetrahedron = new Tetrahedron(
                
                    indices[i],
                    indices[i + 1],
                    indices[i + 2],
                    indices[i + 3]
                );
                tetrahedrons.Add(tetrahedron);
            }

            return tetrahedrons.ToArray();
        }
        
        
        
        // Get the four faces of the tetrahedron
        public Face[] GetFaces()
        {
            return new Face[]
            {
                new Face(Vert0, Vert1, Vert2) { ParentTetrahedron = this }, // Face opposite Vert3
                new Face(Vert0, Vert1, Vert3) { ParentTetrahedron = this }, // Face opposite Vert2
                new Face(Vert0, Vert2, Vert3) { ParentTetrahedron = this }, // Face opposite Vert1
                new Face(Vert1, Vert2, Vert3) { ParentTetrahedron = this }  // Face opposite Vert0
            };
        }
        
        bool IsPointInsideTetrahedron(Vector4 baryCoords)
        {
            return baryCoords.x >= 0 && baryCoords.y >= 0 && baryCoords.z >= 0 && baryCoords.w >= 0;
        }
        
        public class Face
        {
            public int[] Vertices; // Array of 3 vertex indices
            public Tetrahedron ParentTetrahedron; // Reference to the parent tetrahedron
            public int TetrahedronIndex;
            public Face(int v0, int v1, int v2)
            {
                // Store the indices sorted to ensure uniqueness
                Vertices = new int[] {v0, v1, v2};
                System.Array.Sort(Vertices);
            }
            // Checks if the face contains a given vertex index
            public bool ContainsVertex(int vertexIndex)
            {
                return Vertices[0] == vertexIndex || Vertices[1] == vertexIndex || Vertices[2] == vertexIndex;
            }
            // Override Equals and GetHashCode for dictionary operations
            public override bool Equals(object obj)
            {
                if (obj is Face other)
                {
                    return Vertices[0] == other.Vertices[0] &&
                           Vertices[1] == other.Vertices[1] &&
                           Vertices[2] == other.Vertices[2];
                }
                return false;
            }
        
            // public override int GetHashCode()
            // {
            //     return Vertices[0].GetHashCode() ^
            //            Vertices[1].GetHashCode() ^
            //            Vertices[2].GetHashCode();
            // }
            public override int GetHashCode()
            {
                // Since the vertices are sorted, their hash codes can be combined
                int hash = 17;
                hash = hash * 31 + Vertices[0].GetHashCode();
                hash = hash * 31 + Vertices[1].GetHashCode();
                hash = hash * 31 + Vertices[2].GetHashCode();
                return hash;
            }
        }
 
        public static void CalculateNeighbors(Tetrahedron[] tetrahedrons)
        {
        // Step 1: Create a face-to-tetrahedra mapping
        Dictionary<Face, List<int>> faceToTetrahedra = new Dictionary<Face, List<int>>();

        // Step 2: Collect all faces and map them to tetrahedra
        for (int tetIndex = 0; tetIndex < tetrahedrons.Length; tetIndex++)
        {
            Tetrahedron tet = tetrahedrons[tetIndex];

            // Define the four faces of the tetrahedron
            Face[] faces = tet.GetFaces();

            // For each face, add the tetrahedron index to the dictionary
            for (int faceIndex = 0; faceIndex < 4; faceIndex++)
            {
                Face face = faces[faceIndex];

                if (faceToTetrahedra.TryGetValue(face, out var tetList))
                {
                    tetList.Add(tetIndex);
                }
                else
                {
                    faceToTetrahedra[face] = new List<int> {tetIndex};
                }
            }
        }

        // Step 3: Assign neighbors to each tetrahedron
        for (int tetIndex = 0; tetIndex < tetrahedrons.Length; tetIndex++)
        {
            Tetrahedron tet = tetrahedrons[tetIndex];

            // Ensure the Neighbors array is initialized
            if (tet.Neighbors == null || tet.Neighbors.Length != 4)
            {
                tet.Neighbors = new int[4] {-1, -1, -1, -1};
            }

            // Define the four faces of the tetrahedron
            Face[] faces = tet.GetFaces();

            // For each face, find and assign the neighbor
            for (int faceIndex = 0; faceIndex < 4; faceIndex++)
            {
                Face face = faces[faceIndex];

                // Get the list of tetrahedra sharing this face
                List<int> tetList = faceToTetrahedra[face];

                // The current tetrahedron is in tetList; find the other one (if any)
                int neighborIndex = -1;
                foreach (int tIndex in tetList)
                {
                    if (tIndex != tetIndex)
                    {
                        neighborIndex = tIndex;
                        break;
                    }
                }

                // Assign the neighbor index
                tet.Neighbors[faceIndex] = neighborIndex;
            }
        }
    }
    }

//Trying out a 3x3 to remove unimportant identity data  
//
[System.Serializable]
public struct Matrix3x3
{
    public float m00, m01, m02;
    public float m10, m11, m12;
    public float m20, m21, m22;

    public Matrix3x3(
        float m00, float m01, float m02,
        float m10, float m11, float m12,
        float m20, float m21, float m22)
    {
        this.m00 = m00; this.m01 = m01; this.m02 = m02;
        this.m10 = m10; this.m11 = m11; this.m12 = m12;
        this.m20 = m20; this.m21 = m21; this.m22 = m22;
    }

    //Simple cast. Inaccurate labeling and used to quickly swap 3x3 and 4x4 matrix types
    public Vector3 MultiplyPoint3x4(Vector3 point)
    {
       return MultiplyPoint3x3(point);
    }
    
    // Transforms a position by this matrix, without a perspective divide. (fast)
    public Vector3 MultiplyPoint3x3(Vector3 point)
    {
        Vector3 res;
        res.x = m00 * point.x + m01 * point.y + m02 * point.z ;
        res.y = m10 * point.x + m11 * point.y + m12 * point.z ;
        res.z = m20 * point.x + m21 * point.y + m22 * point.z ;
        return res;
    }
    
      // Determinant method
    public float Determinant()
    {
        return
            m00 * (m11 * m22 - m12 * m21) -
            m01 * (m10 * m22 - m12 * m20) +
            m02 * (m10 * m21 - m11 * m20);
    }

    // Inverse method
    public Matrix3x3 Inverse()
    {
        float det = Determinant();

        if (Mathf.Abs(det) < Mathf.Epsilon)
        {
            throw new System.Exception("Matrix is singular and cannot be inverted.");
        }

        float invDet = 1f / det;

        // Cofactors
        float c00 =  (m11 * m22 - m12 * m21);
        float c01 = -(m10 * m22 - m12 * m20);
        float c02 =  (m10 * m21 - m11 * m20);

        float c10 = -(m01 * m22 - m02 * m21);
        float c11 =  (m00 * m22 - m02 * m20);
        float c12 = -(m00 * m21 - m01 * m20);

        float c20 =  (m01 * m12 - m02 * m11);
        float c21 = -(m00 * m12 - m02 * m10);
        float c22 =  (m00 * m11 - m01 * m10);

        // Adjugate (transpose of cofactor matrix)
        Matrix3x3 adjugate = new Matrix3x3(
            c00, c10, c20,
            c01, c11, c21,
            c02, c12, c22
        );

        // Multiply by inverse of determinant
        Matrix3x3 inverse = new Matrix3x3(
            adjugate.m00 * invDet, adjugate.m01 * invDet, adjugate.m02 * invDet,
            adjugate.m10 * invDet, adjugate.m11 * invDet, adjugate.m12 * invDet,
            adjugate.m20 * invDet, adjugate.m21 * invDet, adjugate.m22 * invDet
        );

        return inverse;
    }

    // Optional: Multiply a vector by the matrix
    public static Vector3 MultiplyVector(Matrix3x3 matrix, Vector3 vector)
    {
        return new Vector3(
            matrix.m00 * vector.x + matrix.m01 * vector.y + matrix.m02 * vector.z,
            matrix.m10 * vector.x + matrix.m11 * vector.y + matrix.m12 * vector.z,
            matrix.m20 * vector.x + matrix.m21 * vector.y + matrix.m22 * vector.z
        );
    }

    // Optional: Matrix multiplication
    public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b)
    {
        return new Matrix3x3(
            a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20,
            a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21,
            a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,

            a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20,
            a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21,
            a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,

            a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20,
            a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21,
            a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22
        );
    }
}



public static class Tetrahedralization
{
//     public static void Tetrahedralize(Vector3[] originalPositions, out int[] outIndices, out Vector3[] outPositions)
//     {
//         // // Create a list to hold the vertices, including the super-tetrahedron vertices
//         // List<Vector3> vertices = new List<Vector3>(originalPositions);
//         //
//         // // Initialize the tetrahedralizer with the original positions
//         // DelaunayTetrahedralizer tetrahedralizer = new DelaunayTetrahedralizer(vertices);
//         //
//         // // Perform the tetrahedralization
//         // List<Tetrahedron> tetrahedra = tetrahedralizer.Tetrahedralize();
//         //
//         // // Retrieve the vertices after removing the super-tetrahedron vertices
//         // outPositions = tetrahedralizer.GetVertices().ToArray();
//         //
//         // // Build the outIndices array
//         // List<int> indicesList = new List<int>();
//         //
//         // foreach (Tetrahedron tet in tetrahedra)
//         // {
//         //     indicesList.Add(tet.Vert0);
//         //     indicesList.Add(tet.Vert1);
//         //     indicesList.Add(tet.Vert2);
//         //     indicesList.Add(tet.Vert3);
//         // }
//         //
//         // outIndices = indicesList.ToArray();
//     }
// }

    public static void Tetrahedralize(Vector3[] originalPositions, out int[] outIndices, out Vector3[] outPositions)
    {
        // Initialize output lists
        List<Vector3> positions = new List<Vector3>(originalPositions);
        List<int> indices = new List<int>();
        

        // Step 1: Create Super Tetrahedron
        Tetrahedron superTetrahedron = CreateSuperTetrahedron(positions);

        // Initialize tetrahedra list
        List<Tetrahedron> tetrahedra = new List<Tetrahedron> { superTetrahedron };

        // Step 2: Insert points incrementally
        for (int i = 0; i < originalPositions.Length; i++)
        {
            Vector3 point = originalPositions[i];

            // Locate the tetrahedron containing the point
            Tetrahedron containingTetrahedron = LocateContainingTetrahedron(tetrahedra, point, positions);

            if (containingTetrahedron == null)
            {
                Debug.LogError("Point is outside of the triangulation.");
                continue;
            }

            // Subdivide the containing tetrahedron
            List<Tetrahedron> newTetrahedra = SubdivideTetrahedron(tetrahedra, containingTetrahedron, i, positions);

            // Legalize faces
            LegalizeFaces(tetrahedra, newTetrahedra, i, positions);
        }

        // Step 3: Cleanup
        Cleanup(tetrahedra, positions.Count, positions);

        // Step 4: Prepare output data
        PrepareOutputData(tetrahedra, positions, out positions, out indices);

        // Assign output arrays
        outPositions = positions.ToArray();
        outIndices = indices.ToArray();
    }

    // Helper methods

    static Tetrahedron CreateSuperTetrahedron(List<Vector3> positions)
    {
        // Calculate bounds
        Vector3 min = positions[0];
        Vector3 max = positions[0];

        foreach (Vector3 pos in positions)
        {
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
        }

        Vector3 size = max - min;
        float maxSize = Mathf.Max(size.x, size.y, size.z) * 10f;

        Vector3 center = (min + max) / 2f;

        // Create positions of the super tetrahedron
        Vector3 v0 = center + new Vector3(-maxSize, -maxSize, -maxSize);
        Vector3 v1 = center + new Vector3(maxSize, -maxSize, -maxSize);
        Vector3 v2 = center + new Vector3(0, maxSize, -maxSize);
        Vector3 v3 = center + new Vector3(0, 0, maxSize);

        // Add super tetrahedron vertices to positions list
        int indexV0 = positions.Count;
        positions.Add(v0);
        int indexV1 = positions.Count;
        positions.Add(v1);
        int indexV2 = positions.Count;
        positions.Add(v2);
        int indexV3 = positions.Count;
        positions.Add(v3);

        // Create the super tetrahedron
        return new Tetrahedron(indexV0, indexV1, indexV2, indexV3);
    }

    static Tetrahedron LocateContainingTetrahedron(List<Tetrahedron> tetrahedra, Vector3 point, List<Vector3> positions)
    {
        // Simple linear search (can be optimized with spatial partitioning)
        foreach (Tetrahedron tetra in tetrahedra)
        {
            if (PointInTetrahedron(point, tetra, positions))
            {
                return tetra;
            }
        }
        return null;
    }

    static bool PointInTetrahedron(Vector3 point, Tetrahedron tetrahedron, List<Vector3> positions)
    {
        // Get vertex positions
        Vector3 v0 = positions[tetrahedron.Vert0];
        Vector3 v1 = positions[tetrahedron.Vert1];
        Vector3 v2 = positions[tetrahedron.Vert2];
        Vector3 v3 = positions[tetrahedron.Vert3];

        // Calculate signed volumes
        float v0_sign = SignedVolume(point, v0, v1, v2);
        float v1_sign = SignedVolume(point, v0, v1, v3);
        float v2_sign = SignedVolume(point, v0, v2, v3);
        float v3_sign = SignedVolume(point, v1, v2, v3);

        // If all volumes have the same sign, the point is inside the tetrahedron
        bool hasNeg = (v0_sign < 0) || (v1_sign < 0) || (v2_sign < 0) || (v3_sign < 0);
        bool hasPos = (v0_sign > 0) || (v1_sign > 0) || (v2_sign > 0) || (v3_sign > 0);

        return !(hasNeg && hasPos);
    }

    static float SignedVolume(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        return Vector3.Dot(Vector3.Cross(p2 - p1, p3 - p1), p4 - p1) / 6f;
    }

    static List<Tetrahedron> SubdivideTetrahedron(List<Tetrahedron> tetrahedra, Tetrahedron tetrahedron, int newVertexIndex, List<Vector3> positions)
    {
        // Remove the old tetrahedron
        tetrahedra.Remove(tetrahedron);

        // Create new tetrahedra for each face
        List<Tetrahedron> newTetrahedra = new List<Tetrahedron>();

        Tetrahedron.Face[] faces = tetrahedron.GetFaces();

        foreach (Tetrahedron.Face face in faces)
        {
            Tetrahedron newTetra = new Tetrahedron(face.Vertices[0], face.Vertices[1], face.Vertices[2], newVertexIndex);
            newTetrahedra.Add(newTetra);
            tetrahedra.Add(newTetra);
        }

        // Recalculate neighbors (optional at this stage)
        Tetrahedron.CalculateNeighbors(tetrahedra.ToArray());

        return newTetrahedra;
    }

    static void LegalizeFaces(List<Tetrahedron> tetrahedra, List<Tetrahedron> newTetrahedra, int newVertexIndex, List<Vector3> positions)
    {
        Queue<Tetrahedron.Face> faceQueue = new Queue<Tetrahedron.Face>();

        // Initialize the queue with faces opposite to the new vertex
        foreach (Tetrahedron tetra in newTetrahedra)
        {
            Tetrahedron.Face[] faces = tetra.GetFaces();

            foreach (Tetrahedron.Face face in faces)
            {
                if (!face.ContainsVertex(newVertexIndex))
                {
                    faceQueue.Enqueue(face);
                }
            }
        }

        // Process the faces
        while (faceQueue.Count > 0)
        {
            Tetrahedron.Face face = faceQueue.Dequeue();

            // Find the adjacent tetrahedron sharing this face
            int neighborIndex = FindNeighborTetrahedron(tetrahedra, face, face.TetrahedronIndex);

            if (neighborIndex != -1)
            {
                Tetrahedron neighborTetra = tetrahedra[neighborIndex];

                int oppositeVertexIndex = GetOppositeVertexIndex(neighborTetra, face);

                if (InSphereTest(positions[newVertexIndex], face, positions[oppositeVertexIndex], positions))
                {
                    // Flip the face
                    FlipFace(tetrahedra, face, neighborTetra);

                    // Add new faces to the queue
                    Tetrahedron[] affectedTetrahedra = { face.ParentTetrahedron, neighborTetra };

                    foreach (Tetrahedron tetra in affectedTetrahedra)
                    {
                        foreach (Tetrahedron.Face newFace in tetra.GetFaces())
                        {
                            if (!newFace.ContainsVertex(newVertexIndex))
                            {
                                faceQueue.Enqueue(newFace);
                            }
                        }
                    }
                }
            }
        }
    }

    static int FindNeighborTetrahedron(List<Tetrahedron> tetrahedra, Tetrahedron.Face face, int currentTetraIndex)
    {
        // Look up the neighbor index from the tetrahedron's Neighbors array
        Tetrahedron currentTetra = tetrahedra[currentTetraIndex];

        for (int i = 0; i < currentTetra.Neighbors.Length; i++)
        {
            if (currentTetra.Neighbors[i] != -1 && currentTetra.Neighbors[i] != currentTetraIndex)
            {
                Tetrahedron neighborTetra = tetrahedra[currentTetra.Neighbors[i]];

                Tetrahedron.Face[] neighborFaces = neighborTetra.GetFaces();

                foreach (Tetrahedron.Face neighborFace in neighborFaces)
                {
                    if (FacesAreEqual(face, neighborFace))
                    {
                        return currentTetra.Neighbors[i];
                    }
                }
            }
        }

        return -1;
    }

    static int GetOppositeVertexIndex(Tetrahedron tetrahedron, Tetrahedron.Face face)
    {
        int[] allVertices = { tetrahedron.Vert0, tetrahedron.Vert1, tetrahedron.Vert2, tetrahedron.Vert3 };

        foreach (int vIndex in allVertices)
        {
            if (!face.ContainsVertex(vIndex))
            {
                return vIndex;
            }
        }

        // Should not reach here
        return -1;
    }

    static bool InSphereTest(Vector3 point, Tetrahedron.Face face, Vector3 oppositeVertexPosition, List<Vector3> positions)
    {
        // Use the InSphere test to check if the point lies inside the circumsphere
        // formed by the tetrahedron composed of the face and the opposite vertex

        Vector3 a = positions[face.Vertices[0]];
        Vector3 b = positions[face.Vertices[1]];
        Vector3 c = positions[face.Vertices[2]];
        Vector3 d = oppositeVertexPosition;
        Vector3 e = point;

        // Compute the determinant
        float det = Determinant(
            a.x - e.x, a.y - e.y, a.z - e.z, (a - e).sqrMagnitude,
            b.x - e.x, b.y - e.y, b.z - e.z, (b - e).sqrMagnitude,
            c.x - e.x, c.y - e.y, c.z - e.z, (c - e).sqrMagnitude,
            d.x - e.x, d.y - e.y, d.z - e.z, (d - e).sqrMagnitude
        );

        return det > 0;
    }

    static float Determinant(
        float a11, float a12, float a13, float a14,
        float a21, float a22, float a23, float a24,
        float a31, float a32, float a33, float a34,
        float a41, float a42, float a43, float a44)
    {
        // Compute the determinant of a 4x4 matrix using expansion by minors
        // For brevity, implement this using a mathematical library or write out the full expansion
        // Placeholder implementation:
        // Note: This is a complex calculation, so ensure you implement it correctly.
        float det = a11 * Minor(a21, a22, a23, a24, a31, a32, a33, a34, a41, a42, a43, a44)
                  - a12 * Minor(a21, a22, a23, a24, a31, a32, a33, a34, a41, a42, a43, a44)
                  + a13 * Minor(a21, a22, a23, a24, a31, a32, a33, a34, a41, a42, a43, a44)
                  - a14 * Minor(a21, a22, a23, a24, a31, a32, a33, a34, a41, a42, a43, a44);
        return det;
    }

    static float Minor(
        float b11, float b12, float b13, float b14,
        float b21, float b22, float b23, float b24,
        float b31, float b32, float b33, float b34)
    {
        // Compute the determinant of a 3x3 matrix (minor of the 4x4 matrix)
        // Implement the calculation of the determinant of a 3x3 matrix
        // Placeholder implementation:
        float det = b11 * (b22 * b33 - b23 * b32)
                  - b12 * (b21 * b33 - b23 * b31)
                  + b13 * (b21 * b32 - b22 * b31);
        return det;
    }

    static void FlipFace(List<Tetrahedron> tetrahedra, Tetrahedron.Face face, Tetrahedron neighborTetrahedron)
    {
        // Implement face flipping by retriangulating the cavity formed by the two tetrahedra sharing the face

        // Remove the two tetrahedra
        tetrahedra.Remove(face.ParentTetrahedron);
        tetrahedra.Remove(neighborTetrahedron);

        // Get indices of vertices
        int a = face.Vertices[0];
        int b = face.Vertices[1];
        int c = face.Vertices[2];
        int d = GetOppositeVertexIndex(face.ParentTetrahedron, face);
        int e = GetOppositeVertexIndex(neighborTetrahedron, face);

        // Create new tetrahedra
        Tetrahedron newTetra1 = new Tetrahedron(a, b, d, e);
        Tetrahedron newTetra2 = new Tetrahedron(a, c, d, e);
        Tetrahedron newTetra3 = new Tetrahedron(b, c, d, e);

        // Add new tetrahedra to the list
        tetrahedra.Add(newTetra1);
        tetrahedra.Add(newTetra2);
        tetrahedra.Add(newTetra3);

        // Recalculate neighbors for the affected tetrahedra
        Tetrahedron.CalculateNeighbors(tetrahedra.ToArray());
    }

    static bool FacesAreEqual(Tetrahedron.Face f1, Tetrahedron.Face f2)
    {
        return f1.Equals(f2);
    }

    static void Cleanup(List<Tetrahedron> tetrahedra, int originalVertexCount, List<Vector3> positions)
    {
        // Identify indices of the super-tetrahedron's vertices
        HashSet<int> superVertices = new HashSet<int>
        {
            originalVertexCount,     // index of v0 of super tetrahedron
            originalVertexCount + 1, // index of v1
            originalVertexCount + 2, // index of v2
            originalVertexCount + 3  // index of v3
        };

        // Remove tetrahedra that include any super-vertex
        tetrahedra.RemoveAll(tetra =>
            superVertices.Contains(tetra.Vert0) ||
            superVertices.Contains(tetra.Vert1) ||
            superVertices.Contains(tetra.Vert2) ||
            superVertices.Contains(tetra.Vert3));

        // Remove super-tetrahedron vertices from positions list
        positions.RemoveRange(originalVertexCount, 4);
    }

    static void PrepareOutputData(List<Tetrahedron> tetrahedra, List<Vector3> positions, out List<Vector3> outPositions, out List<int> outIndices)
    {
        outPositions = new List<Vector3>(positions);
        outIndices = new List<int>();

        foreach (Tetrahedron tetra in tetrahedra)
        {
            outIndices.Add(tetra.Vert0);
            outIndices.Add(tetra.Vert1);
            outIndices.Add(tetra.Vert2);
            outIndices.Add(tetra.Vert3);
        }
    }
}


// public class DelaunayTetrahedralizer
// {
//     private List<Tetrahedron> tetrahedra;
//     private List<Vector3> vertices;
//     private int superVertexStartIndex;
//
//     public DelaunayTetrahedralizer(List<Vector3> points)
//     {
//         vertices = points;
//         tetrahedra = new List<Tetrahedron>();
//
//         // Initialize the super-tetrahedron
//         InitializeSuperTetrahedron();
//     }
//
//     public List<Tetrahedron> Tetrahedralize()
//     {
//         // Insert each point into the tetrahedralization
//         int originalPointCount = superVertexStartIndex;
//         for (int i = 0; i < originalPointCount; i++)
//         {
//             InsertPoint(i);
//         }
//
//         // Remove tetrahedra that include super-tetrahedron vertices
//         RemoveSuperTetrahedron();
//
//         return tetrahedra;
//     }
//
//     private void InitializeSuperTetrahedron()
//     {
//         // Find the bounding box of all points
//         Vector3 min = vertices[0];
//         Vector3 max = vertices[0];
//
//         foreach (Vector3 point in vertices)
//         {
//             min = Vector3.Min(min, point);
//             max = Vector3.Max(max, point);
//         }
//
//         // Adjust the expansion factor if necessary
//         float expansionFactor = 10f;
//
//         // Expand the bounding box
//         Vector3 size = max - min;
//         float maxDim = Mathf.Max(size.x, size.y, size.z);
//         min -= Vector3.one * maxDim * expansionFactor;
//         max += Vector3.one * maxDim * expansionFactor;
//
//         // Define the super-tetrahedron vertices
//         Vector3 v0 = new Vector3(min.x - 10 * maxDim, min.y - 10 * maxDim, min.z - 10 * maxDim);
//         Vector3 v1 = new Vector3(max.x + 10 * maxDim, min.y - 10 * maxDim, min.z - 10 * maxDim);
//         Vector3 v2 = new Vector3(min.x - 10 * maxDim, max.y + 10 * maxDim, min.z - 10 * maxDim);
//         Vector3 v3 = new Vector3(min.x - 10 * maxDim, min.y - 10 * maxDim, max.z + 10 * maxDim);
//
//         // Record the starting index of the super-tetrahedron vertices
//         superVertexStartIndex = vertices.Count;
//
//         // Add these vertices to the vertex list
//         vertices.Add(v0);
//         vertices.Add(v1);
//         vertices.Add(v2);
//         vertices.Add(v3);
//
//         // Create the super-tetrahedron and add it to the tetrahedra list
//         Tetrahedron superTet = new Tetrahedron(superVertexStartIndex, superVertexStartIndex + 1, superVertexStartIndex + 2, superVertexStartIndex + 3);
//         tetrahedra.Add(superTet);
//         
//         Debug.Log(tetrahedra.Count + " tetrahedra counf, " 
//                                    + vertices.Count +" vertices conut : "
//                                    + superVertexStartIndex + " :superVertexStartIndex ");
//     }
//
//     private void InsertPoint(int pointIndex)
//     {
//         Vector3 point = vertices[pointIndex];
//         List<Tetrahedron> badTetrahedra = FindBadTetrahedra(point);
//
//         if (badTetrahedra.Count == 0)
//         {
//             // The point lies outside the convex hull.
//             // Identify visible faces and create new tetrahedra.
//             List<Tetrahedron.Face> visibleFaces = FindVisibleFaces(point);
//
//             foreach (Tetrahedron.Face face in visibleFaces)
//             {
//                 Tetrahedron newTet = new Tetrahedron(face.Vertices[0], face.Vertices[1], face.Vertices[2], pointIndex);
//                 tetrahedra.Add(newTet);
//
//                 // Debugging output
//                 Debug.Log($"Added Tetrahedron (Outside Convex Hull): {newTet.Vert0}, {newTet.Vert1}, {newTet.Vert2}, {newTet.Vert3}");
//             }
//         }
//         else
//         {
//             // Remove bad tetrahedra
//             foreach (Tetrahedron badTet in badTetrahedra)
//             {
//                 tetrahedra.Remove(badTet);
//                 // Debugging output
//                 Debug.Log($"Removed Bad Tetrahedron: {badTet.Vert0}, {badTet.Vert1}, {badTet.Vert2}, {badTet.Vert3}");
//             }
//
//             // Find the boundary faces of the cavity
//             List<Tetrahedron.Face> boundaryFaces = FindBoundaryFaces(badTetrahedra);
//
//             // Re-triangulate the cavity
//             // Re-triangulate the cavity
//             foreach (Tetrahedron.Face face in boundaryFaces)
//             {
//                 // Allow faces with super-tetrahedron vertices during initial insertions
//                 Tetrahedron newTet = new Tetrahedron(face.Vertices[0], face.Vertices[1], face.Vertices[2], pointIndex);
//                 tetrahedra.Add(newTet);
//
//                 // Debugging output
//                 Debug.Log($"Added Tetrahedron: {newTet.Vert0}, {newTet.Vert1}, {newTet.Vert2}, {newTet.Vert3}");
//             }
//
//         }
//     }
//     
//     private List<Tetrahedron.Face> FindVisibleFaces(Vector3 point)
//     {
//         List<Tetrahedron.Face> visibleFaces = new List<Tetrahedron.Face>();
//
//         // Get all faces on the convex hull
//         HashSet<Tetrahedron.Face> convexHullFaces = GetConvexHullFaces();
//
//         foreach (Tetrahedron.Face face in convexHullFaces)
//         {
//             // Skip faces with super-tetrahedron vertices
//             if (face.Vertices.Any(v => v >= superVertexStartIndex))
//                 continue;
//
//             Vector3 a = vertices[face.Vertices[0]];
//             Vector3 b = vertices[face.Vertices[1]];
//             Vector3 c = vertices[face.Vertices[2]];
//
//             // Compute the normal of the face
//             Vector3 normal = Vector3.Cross(b - a, c - a);
//
//             // Determine if the face is visible from the point
//             if (Vector3.Dot(normal, point - a) > 0)
//             {
//                 visibleFaces.Add(face);
//             }
//         }
//
//         return visibleFaces;
//     }
//     private HashSet<Tetrahedron.Face> GetConvexHullFaces()
//     {
//         HashSet<Tetrahedron.Face> faceSet = new HashSet<Tetrahedron.Face>();
//
//         foreach (Tetrahedron tet in tetrahedra)
//         {
//             foreach (Tetrahedron.Face face in tet.GetFaces())
//             {
//                 if (faceSet.Contains(face))
//                 {
//                     // Internal face (shared by two tetrahedra)
//                     faceSet.Remove(face);
//                 }
//                 else
//                 {
//                     // Boundary face
//                     faceSet.Add(face);
//                 }
//             }
//         }
//
//         return faceSet;
//     }
//
//
//
//     private List<Tetrahedron> FindBadTetrahedra(Vector3 point)
//     {
//         List<Tetrahedron> badTetrahedra = new List<Tetrahedron>();
//
//         foreach (Tetrahedron tet in tetrahedra)
//         {
//             Circumsphere sphere = new Circumsphere(
//                 vertices[tet.Vert0],
//                 vertices[tet.Vert1],
//                 vertices[tet.Vert2],
//                 vertices[tet.Vert3]
//             );
//
//             if (sphere.Contains(point))
//             {
//                 badTetrahedra.Add(tet);
//             }
//         }
//
//         return badTetrahedra;
//     }
//
//     private List<Tetrahedron.Face> FindBoundaryFaces(List<Tetrahedron> badTetrahedra)
//     {
//         Dictionary<Tetrahedron.Face, int> faceCount = new Dictionary<Tetrahedron.Face, int>();
//
//         foreach (Tetrahedron tet in badTetrahedra)
//         {
//             // Get the four faces of the tetrahedron
//             Tetrahedron.Face[] faces = tet.GetFaces();
//
//             foreach (Tetrahedron.Face face in faces)
//             {
//                 if (faceCount.ContainsKey(face))
//                 {
//                     faceCount[face]++;
//                 }
//                 else
//                 {
//                     faceCount[face] = 1;
//                 }
//             }
//         }
//
//         // Boundary faces are those that appear exactly once
//         List<Tetrahedron.Face> boundaryFaces = new List<Tetrahedron.Face>();
//
//         foreach (var kvp in faceCount)
//         {
//             if (kvp.Value == 1)
//             {
//                 boundaryFaces.Add(kvp.Key);
//             }
//         }
//
//         return boundaryFaces;
//     }
//
//     private void RemoveSuperTetrahedron()
//     {
//         // Remove any tetrahedra that include super-tetrahedron vertices
//         tetrahedra.RemoveAll(tet =>
//             tet.Vert0 >= superVertexStartIndex ||
//             tet.Vert1 >= superVertexStartIndex ||
//             tet.Vert2 >= superVertexStartIndex ||
//             tet.Vert3 >= superVertexStartIndex
//         );
//
//         // Optionally, remove the super-tetrahedron vertices from the vertex list
//         vertices.RemoveRange(superVertexStartIndex, 4);
//
//         // Adjust indices in tetrahedra to account for removed vertices
//         foreach (Tetrahedron tet in tetrahedra)
//         {
//             if (tet.Vert0 >= superVertexStartIndex)
//                 tet.Vert0 -= 4;
//             if (tet.Vert1 >= superVertexStartIndex)
//                 tet.Vert1 -= 4;
//             if (tet.Vert2 >= superVertexStartIndex)
//                 tet.Vert2 -= 4;
//             if (tet.Vert3 >= superVertexStartIndex)
//                 tet.Vert3 -= 4;
//         }
//
//         // Update the vertex list to exclude the super-tetrahedron vertices
//         vertices = vertices.GetRange(0, superVertexStartIndex);
//     }
//
//     public List<Vector3> GetVertices()
//     {
//         return vertices;
//     }
//     
//         // Circumsphere class to compute the circumsphere of a tetrahedron
//     public class Circumsphere
//     {
//         public Vector3 Center;
//         public float RadiusSquared;
//     
//         public Circumsphere(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
//         {
//             // Compute the circumsphere of the tetrahedron defined by points a, b, c, d
//             // Using linear algebra to solve for the center
//     
//             Matrix4x4 mat = new Matrix4x4();
//     
//             mat[0, 0] = a.x - d.x;
//             mat[0, 1] = a.y - d.y;
//             mat[0, 2] = a.z - d.z;
//             mat[0, 3] = (a - d).sqrMagnitude;
//     
//             mat[1, 0] = b.x - d.x;
//             mat[1, 1] = b.y - d.y;
//             mat[1, 2] = b.z - d.z;
//             mat[1, 3] = (b - d).sqrMagnitude;
//     
//             mat[2, 0] = c.x - d.x;
//             mat[2, 1] = c.y - d.y;
//             mat[2, 2] = c.z - d.z;
//             mat[2, 3] = (c - d).sqrMagnitude;
//     
//             mat[3, 0] = 0;
//             mat[3, 1] = 0;
//             mat[3, 2] = 0;
//             mat[3, 3] = 1;
//     
//             // Compute the minors needed for the center
//             float a11 = Determinant3x3(
//                 mat[1, 1], mat[1, 2], mat[1, 3],
//                 mat[2, 1], mat[2, 2], mat[2, 3],
//                 mat[3, 1], mat[3, 2], mat[3, 3]);
//     
//             float a12 = -Determinant3x3(
//                 mat[1, 0], mat[1, 2], mat[1, 3],
//                 mat[2, 0], mat[2, 2], mat[2, 3],
//                 mat[3, 0], mat[3, 2], mat[3, 3]);
//     
//             float a13 = Determinant3x3(
//                 mat[1, 0], mat[1, 1], mat[1, 3],
//                 mat[2, 0], mat[2, 1], mat[2, 3],
//                 mat[3, 0], mat[3, 1], mat[3, 3]);
//     
//             float a14 = -Determinant3x3(
//                 mat[1, 0], mat[1, 1], mat[1, 2],
//                 mat[2, 0], mat[2, 1], mat[2, 2],
//                 mat[3, 0], mat[3, 1], mat[3, 2]);
//     
//             float det = mat[0, 0] * a11 + mat[0, 1] * a12 + mat[0, 2] * a13 + mat[0, 3] * a14;
//     
//             if (Mathf.Abs(det) < Mathf.Epsilon)
//             {
//                 // Points are coplanar or nearly so; handle degenerate case
//                 Center = Vector3.zero;
//                 RadiusSquared = float.PositiveInfinity;
//             }
//             else
//             {
//                 Center = new Vector3(
//                     0.5f * a11 / det,
//                     0.5f * a12 / det,
//                     0.5f * a13 / det
//                 ) + d;
//     
//                 RadiusSquared = (Center - a).sqrMagnitude;
//             }
//         }
//     
//         // Helper method to compute the determinant of a 3x3 matrix
//         private float Determinant3x3(
//             float a00, float a01, float a02,
//             float a10, float a11, float a12,
//             float a20, float a21, float a22)
//         {
//             return a00 * (a11 * a22 - a12 * a21)
//                  - a01 * (a10 * a22 - a12 * a20)
//                  + a02 * (a10 * a21 - a11 * a20);
//         }
//     
//         // Check if the point is inside the circumsphere
//         public bool Contains(Vector3 point)
//         {
//             return (point - Center).sqrMagnitude <= RadiusSquared + Mathf.Epsilon;
//         }
//     }
    
    // public class Circumsphere
    // {
    //     public Vector3 Center;
    //     public float RadiusSquared;
    //
    //     public Circumsphere(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    //     {
    //         // Edge vectors
    //         Vector3 ba = b - a;
    //         Vector3 ca = c - a;
    //         Vector3 da = d - a;
    //
    //         // Cross products
    //         Vector3 crossBC = Vector3.Cross(ba, ca);
    //         Vector3 crossCD = Vector3.Cross(ca, da);
    //         Vector3 crossDB = Vector3.Cross(da, ba);
    //
    //         // Calculate the numerator components
    //         float numeratorX = (Vector3.Dot(crossBC, da) * ba.sqrMagnitude);
    //         float numeratorY = (Vector3.Dot(crossCD, ba) * ca.sqrMagnitude);
    //         float numeratorZ = (Vector3.Dot(crossDB, ca) * da.sqrMagnitude);
    //
    //         // Denominator
    //         float denominator = 2 * Vector3.Dot(ba, Vector3.Cross(ca, da));
    //
    //         if (Mathf.Abs(denominator) < Mathf.Epsilon)
    //         {
    //             // Degenerate case
    //             Center = Vector3.zero;
    //             RadiusSquared = float.PositiveInfinity;
    //         }
    //         else
    //         {
    //             Center = a + ((crossBC * numeratorX + crossCD * numeratorY + crossDB * numeratorZ) / denominator);
    //
    //             // Calculate radius squared
    //             RadiusSquared = (Center - a).sqrMagnitude;
    //         }
    //     }
    //
    //     public bool Contains(Vector3 point)
    //     {
    //         return (point - Center).sqrMagnitude <= RadiusSquared + Mathf.Epsilon;
    //     }
    // }

//}


   