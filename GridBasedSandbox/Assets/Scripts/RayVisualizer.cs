using UnityEngine;

public class RayVisualizer : MonoBehaviour
{
    private Camera playerCamera;

    public float maxRayDistance = 5f;

    public GameObject hitObjectPrefab;

    private GameObject hitObject;



    private void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        
        if (hitObjectPrefab != null)
        {
            hitObject = Instantiate(hitObjectPrefab, Vector3.zero, Quaternion.identity);
            hitObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("RayVisualizer: Prefab para visualização de hit não está atribuído!");
        }
    }

    private void Update()
    {
        PerformRay();
    }

    private void PerformRay()
    {
        // Proteção contra referências nulas
        if (playerCamera == null)
        {
            Debug.LogWarning("RayVisualizer: Câmera não está referenciada!");
            return;
        }

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxRayDistance))
        {
            hitObject.SetActive(true);
            hitObject.transform.position = hit.point;
        }
        else
        {
            hitObject.SetActive(false);
        }
        
    }
}
