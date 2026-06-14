using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private GameObject InventoryBarUI;
    [SerializeField] private GameObject slotUIPrefab;
    [SerializeField] private Transform slotsParent;

    [Header("Sprites de Fundo")]
    [SerializeField] private Sprite defaultSlotSprite; // Sprite para o fundo normal do slot
    [SerializeField] private Sprite selectedSlotSprite; // Sprite para o fundo do slot selecionado
    
    // Lista para guardar os GameObjects dos slots que aparecem na tela
    private List<GameObject> slotUIObjects = new List<GameObject>();
    
    private void Awake()
    {
        InventoryBarUI.SetActive(true);
    }

    public void CreateBarSlotUI(int slotID, ItemStack itemStack)
    {
        if (slotUIObjects.Count >= 10)
        {
            Debug.LogWarning($"Não é possível criar mais de 10 slots. SlotID {slotID} não criado.");
            return;
        }

        GameObject slotUIObj = Instantiate(slotUIPrefab, slotsParent);
        slotUIObj.name = $"Slot_{slotID}";
        
        // Garante que o slot recém-criado começa com o sprite padrão
        if (slotUIObj.TryGetComponent<Image>(out Image slotBackground))
        {
            slotBackground.sprite = defaultSlotSprite;
        }

        slotUIObjects.Add(slotUIObj);

        UpdateSlotUI(slotID, itemStack);
    }

    // Método para atualizar o ícone do item (continua igual)
    public void UpdateSlotUI(int slotID, ItemStack itemStack)
    {
        if (slotID < 0 || slotID >= slotUIObjects.Count) return;

        GameObject slotUIObj = slotUIObjects[slotID];
        Image iconImage = slotUIObj.transform.GetChild(0).GetComponent<Image>();

        if (itemStack == null || itemStack.itemData == null)
        {
            iconImage.sprite = null;
            iconImage.gameObject.SetActive(false);
        }
        else
        {
            iconImage.sprite = itemStack.itemData.icon;
            iconImage.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Atualiza o sprite de fundo do novo slot selecionado e do slot que foi desmarcado.
    /// </summary>
    public void UpdateSelectionVisual(int newSelectedSlotID, int oldSelectedSlotID)
    {
        // 1. Desativa o destaque do slot antigo (segurança de índice)
        if (oldSelectedSlotID >= 0 && oldSelectedSlotID < slotUIObjects.Count)
        {
            GameObject oldSlot = slotUIObjects[oldSelectedSlotID];
            if (oldSlot.TryGetComponent<Image>(out Image oldBackground))
            {
                oldBackground.sprite = defaultSlotSprite;
            }
        }

        // 2. Ativa o destaque do novo slot selecionado (segurança de índice)
        if (newSelectedSlotID >= 0 && newSelectedSlotID < slotUIObjects.Count)
        {
            GameObject newSlot = slotUIObjects[newSelectedSlotID];
            if (newSlot.TryGetComponent<Image>(out Image newBackground))
            {
                newBackground.sprite = selectedSlotSprite;
            }
        }
    }

    public void ToggleInventoryBar(bool value)
    {
        InventoryBarUI.SetActive(value);
    }
}