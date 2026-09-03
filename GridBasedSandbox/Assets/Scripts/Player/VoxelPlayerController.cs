using UnityEngine;

/// <summary>
/// Controlador de Jogador em Primeira Pessoa para jogos de Voxels.
/// Gerencia física via Rigidbody e rotação do pivô da visão sem causar trepidações.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class VoxelPlayerController : MonoBehaviour
{
    [Header("Configurações de Movimento")]
    [Tooltip("Velocidade de caminhada do jogador em blocos por segundo.")]
    [SerializeField] private float moveSpeed = 4.3f;

    [Tooltip("Velocidade ao correr segurando a tecla Shift.")]
    [SerializeField] private float sprintSpeed = 5.6f;

    [Tooltip("Força do pulo.")]
    [SerializeField] private float jumpForce = 5.5f;

    [Header("Cinemachine e Visão")]
    [Tooltip("Objeto vazio 'CameraPivot' (filho do Player) localizado na altura dos olhos.")]
    [SerializeField] private Transform cameraPivot;

    [Tooltip("Sensibilidade do movimento do mouse.")]
    [SerializeField] private float mouseSensitivity = 2.0f;

    [Tooltip("Limite máximo para olhar para cima e para baixo em graus.")]
    [SerializeField] private float maxLookAngle = 89.0f;

    [Header("Detecção do Chão")]
    [Tooltip("Layer atribuída aos blocos do mapa.")]
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("Distância extra abaixo dos pés para detectar contato com o chão.")]
    [SerializeField] private float groundCheckDistance = 0.1f;

    // Componentes de física internos
    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;

    // Variáveis de estado e controle
    private float cameraPitch = 0.0f;
    private bool isGrounded;
    private Vector2 inputDirection;
    private bool jumpRequested;
    private bool isSprinting;

    private void Start()
    {
        // Resgata referências essenciais do jogador
        rb = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();

        // Bloqueia e esconde o cursor do mouse na tela
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Configurações de física obrigatórias para suavidade e estabilidade
        rb.freezeRotation = true; // Impede o corpo de tombar com colisões
        rb.interpolation = RigidbodyInterpolation.Interpolate; // Suaviza o movimento entre quadros da física
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // Evita atravessar blocos
    }

    private void Update()
    {
        // Lê as entradas do teclado e mouse a cada quadro de renderização
        ProcessInputs();

        // Controla a rotação do corpo (horizontal) e do pivô (vertical)
        HandleLook();
    }

    private void FixedUpdate()
    {
        // Executa atualizações do motor de física
        CheckGrounded();
        HandleMovement();
        HandleJump();
    }

    /// <summary>
    /// Lê os comandos enviados pelo teclado e pelo mouse.
    /// </summary>
    private void ProcessInputs()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        inputDirection = new Vector2(moveX, moveZ).normalized;

        isSprinting = Input.GetKey(KeyCode.LeftShift);

        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            jumpRequested = true;
        }
    }

    /// <summary>
    /// Gira o corpo do jogador na horizontal e o CameraPivot na vertical.
    /// </summary>
    private void HandleLook()
    {
        if (cameraPivot == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Rotação Horizontal (Eixo Y): Gira o corpo do jogador
        transform.Rotate(Vector3.up * mouseX);

        // Rotação Vertical (Eixo X): Gira apenas o pivô da cabeça
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -maxLookAngle, maxLookAngle);
        cameraPivot.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    /// <summary>
    /// Aplica a velocidade de movimento mantendo a gravidade intacta.
    /// </summary>
    private void HandleMovement()
    {
        Vector3 moveDir = (transform.forward * inputDirection.y + transform.right * inputDirection.x);
        float speed = isSprinting ? sprintSpeed : moveSpeed;

        Vector3 targetVelocity = moveDir * speed;
        Vector3 currentVelocity = rb.linearVelocity;

        Vector3 velocityChange = new Vector3(
            targetVelocity.x - currentVelocity.x,
            0f,
            targetVelocity.z - currentVelocity.z
        );

        rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    /// <summary>
    /// Executa a força do pulo.
    /// </summary>
    private void HandleJump()
    {
        if (jumpRequested)
        {
            Vector3 currentVel = rb.linearVelocity;
            currentVel.y = 0f;
            rb.linearVelocity = currentVel;

            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            jumpRequested = false;
        }
    }

    /// <summary>
    /// Checa o contato com o chão na base do colisor do jogador.
    /// </summary>
    private void CheckGrounded()
    {
        Vector3 rayOrigin = transform.position + capsuleCollider.center;
        float rayDistance = (capsuleCollider.height / 2f) - capsuleCollider.radius + groundCheckDistance;

        isGrounded = Physics.SphereCast(
            rayOrigin,
            capsuleCollider.radius * 0.9f,
            Vector3.down,
            out RaycastHit hit,
            rayDistance,
            groundLayer
        );
    }

    /// <summary>
    /// Desenha o indicador visual de checagem do chão no modo Scene.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (capsuleCollider == null) capsuleCollider = GetComponent<CapsuleCollider>();

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Vector3 rayOrigin = transform.position + capsuleCollider.center;
        float rayDistance = (capsuleCollider.height / 2f) - capsuleCollider.radius + groundCheckDistance;

        Gizmos.DrawWireSphere(rayOrigin + Vector3.down * rayDistance, capsuleCollider.radius * 0.9f);
    }
}