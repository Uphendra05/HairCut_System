using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EzySlice;
using HairStudio;
public class MeshCuttingSam : MonoBehaviour
{
    public GameObject box;

    public GameObject hair;

    public GameObject plane;

    public HairRenderer rend;
    void Start()
    {
        hair = rend.BuildFilter(rend.verts, rend.normals, rend.uvs, rend.indices).gameObject;
        hair.GetComponent<MeshFilter>().sharedMesh = rend.BuildFilter(rend.verts, rend.normals, rend.uvs, rend.indices).gameObject.GetComponent<MeshFilter>().mesh;
        hair.AddComponent<Sliceable>();
        Instantiate(hair, transform.position, Quaternion.identity);

    }

   
    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {

            SlicedHull hull = Slice(hair, null);
            if (hull != null)
            {
                Debug.Log("in");
                GameObject bottom = hull.CreateLowerHull(hair, null);
                GameObject top = hull.CreateUpperHull(hair, null);
               // Destroy(hair);
            }
            else
            {
                Debug.Log("Hull is missing");
            }


            //SlicedHull hull = Slice(box, null);
            //if (hull != null)
            //{
            //    Debug.Log("in");
            //    GameObject bottom = hull.CreateLowerHull(box, null);
            //    GameObject top = hull.CreateUpperHull(box, null);
            //    Destroy(box);
            //}
            //else
            //{
            //    Debug.Log("Hull is missing");
            //}




        }
    }


    public SlicedHull Slice(GameObject hair, Material mat = null)
    {
        return hair.Slice(plane.transform.position, plane.transform.up, mat = null);
    }
}
