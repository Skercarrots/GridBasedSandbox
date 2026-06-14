using UnityEngine;

public class GridInteractor : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Arraste o objeto que contém o script GridSystem aqui.")]
    public GridSystem gridSystem;
    
    [Tooltip("A câmera usada para atirar o raio. Geralmente a Main Camera.")]
    public Camera playerCamera;

    [Header("Configurações de Interação")]
    [Tooltip("Distância máxima que o raio alcançará em metros.")]
    public float maxInteractDistance = 5f;
    
    [Tooltip("Quais camadas (Layers) o raio pode atingir?")]
    public LayerMask interactableLayer = Physics.DefaultRaycastLayers;

    void Update()
    {
        // Verifica se o botão esquerdo do mouse foi clicado
        if (Input.GetMouseButtonDown(0))
        {
            //PerformGridRaycast();
        }
    }

    private void PerformGridRaycast()
    {
        // Proteção contra referências nulas
        if (playerCamera == null || gridSystem == null)
        {
            Debug.LogWarning("GridInteractor: Câmera ou GridSystem não estão referenciados!");
            return;
        }

        // Cria um raio que sai da câmera na direção do ponteiro do mouse
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Atira o raio com o limite de distância especificado
        if (Physics.Raycast(ray, out hit, maxInteractDistance, interactableLayer))
        {
            // 1. Aplica a Tolerância de Ponto Flutuante (Epsilon)
            Vector3 placementPosition = hit.point + (hit.normal * 0.01f);

            // 2. Converte o ponto exato do mundo para o índice discreto do Grid
            Vector3Int gridIndex = gridSystem.WorldToGridPosition(placementPosition);

            // 3. (Opcional) Pega o centro exato da célula para instanciar algo depois
            // Vector3 cellCenterWorldPosition = gridSystem.GridToWorldPosition(gridIndex);

            // Debug.Log($"Atingiu: {hit.collider.name}. Índice no Grid: {gridIndex}. Centro Mundial: {cellCenterWorldPosition}");
            Debug.Log($"Atingiu: {hit.collider.name}. Índice no Grid: {gridIndex}");
            // Aqui você chamará o gridSystem.GetOrCreateCell() futuramente para salvar dados.
        }
    }
}