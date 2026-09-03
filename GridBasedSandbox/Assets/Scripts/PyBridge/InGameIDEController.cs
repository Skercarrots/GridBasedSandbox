// InGameIDEController.cs
// Controlador da interface de programação in-game.
// Gerencia a abertura da janela, leitura do código do jogador e limpeza de bugs de formatação.

using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class InGameIDEController : MonoBehaviour
{
    [Header("Referências Core")]
    [Tooltip("Arraste o componente ScriptRunner que executará o código")]
    public ScriptRunner runner; 

    [Header("Elementos da Interface")]
    [Tooltip("O painel principal (fundo) da sua IDE")]
    public GameObject idePanel; 
    
    [Tooltip("O campo de texto grande onde o jogador digita o Python")]
    public TMP_InputField codeInputField; 
    
    public UnityEngine.UI.Button runButton;
    public UnityEngine.UI.Button stopButton;
    public UnityEngine.UI.Button closeButton;

    // Documentação: O método Start é chamado no primeiro frame. 
    // Além de configurar os botões, agora ele assina o evento de mudança de texto para limpar bugs de colagem.
    private void Start()
    {
        if (runButton != null) runButton.onClick.AddListener(RunCode);
        if (stopButton != null) stopButton.onClick.AddListener(StopCode);
        if (closeButton != null) closeButton.onClick.AddListener(ToggleIDE);

        // CONFIGURAÇÃO NOVA: Filtro de Correção de Quebra de Linha
        if (codeInputField != null)
        {
            // Forçamos via código que ele aceite múltiplas linhas, para evitar configurações erradas no Inspector
            codeInputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            
            // Assinamos o nosso método SanitizeText para rodar SEMPRE que o texto for alterado (digitado ou colado)
            codeInputField.onValueChanged.AddListener(SanitizeText);
        }

        if (idePanel != null)
        {
            idePanel.SetActive(false);
        }
    }

    // Documentação: Verifica se a tecla de aspas (') ou crase (`) foi pressionada para abrir/fechar a IDE.
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Quote) || Input.GetKeyDown(KeyCode.BackQuote))
        {
            ToggleIDE();
        }
    }

    // Documentação: Extrai o texto digitado na UI e envia para o ScriptRunner compilar e executar.
    public void RunCode()
    {
        if (runner != null && codeInputField != null)
        {
            string code = codeInputField.text;
            runner.RunScript(code);
            Debug.Log("Código enviado para o ScriptRunner!");
        }
        else
        {
            Debug.LogError("Erro: Faltam referências do ScriptRunner ou do InputField no Inspector.");
        }
    }

    // Documentação: Interrompe imediatamente o script que está rodando no momento.
    public void StopCode()
    {
        if (runner != null)
        {
            runner.StopScript();
            Debug.Log("O script foi parado pelo jogador.");
        }
    }

    // Documentação: Alterna a visibilidade da janela da IDE e ajusta o cursor do mouse.
    public void ToggleIDE()
    {
        if (idePanel == null) return;

        bool isCurrentlyActive = idePanel.activeSelf;
        bool willBeActive = !isCurrentlyActive;
        
        idePanel.SetActive(willBeActive);

        if (willBeActive)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ── Novo Método: Sanitização de Texto ─────────────────────────────────

    // Documentação: Recebe a string atualizada do InputField e remove o caractere '\r'.
    // Isso previne o bug onde o TextMeshPro para de registrar a tecla Enter após colarmos código.
    public void SanitizeText(string currentText)
    {
        // Se encontrarmos o caractere invisível problemático (\r)...
        if (currentText.Contains("\r"))
        {
            // 1. Salvamos a posição atual do "pauzinho que pisca" (cursor/caret) 
            // para que ele não pule para o final do texto irritando o jogador.
            int caretPos = codeInputField.caretPosition;

            // 2. Removemos todos os '\r' da string.
            string cleanedText = currentText.Replace("\r", "");

            // 3. Removemos temporariamente este "ouvinte" (listener)
            // Por que? Porque se mudarmos o texto na linha abaixo, este método seria chamado de novo,
            // criando um loop infinito travando o jogo.
            codeInputField.onValueChanged.RemoveListener(SanitizeText);
            
            // 4. Aplicamos o texto limpo de volta à interface
            codeInputField.text = cleanedText;
            
            // 5. Devolvemos o cursor para a posição em que o jogador estava
            codeInputField.caretPosition = caretPos;
            
            // 6. Religamos o "ouvinte" para as próximas vezes que o jogador digitar ou colar.
            codeInputField.onValueChanged.AddListener(SanitizeText);
        }
    }
}