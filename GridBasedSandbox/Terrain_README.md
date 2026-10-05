# 🌍 Voxel World Terrain Generation System

> **Documentação Técnica do Sistema de Geração Procedural de Terreno**  
> *GridBasedSandbox — Arquitetura de Terreno Multibioma, Cavernas 3D, Minérios Determinísticos e Estruturas entre Chunks.*

---

## 1. Visão Geral da Arquitetura

O sistema de terreno do projeto foi projetado como um pipeline procedural multithread e determinístico, fortemente inspirado na geração moderna do Minecraft (1.18+) com shaping multiclima, corte 3D de cavernas, densidade tridimensional e estampagem contínua de estruturas entre divisas de chunks.

### Pilares Fundamentais:
1. **Thread-Safety Total**: A geração de blocos (`WorldGenerator.Generate()`) executa assincronamente em worker threads gerenciadas pelo `ChunkGenerationScheduler`. Todos os ScriptableObjects tornam-se somente-leitura após a chamada `Prepare()`.
2. **Determinismo Estrito**: Chunks idênticos geram os exatos mesmos blocos em qualquer sessão, ordem de carregamento ou rotação da câmera do jogador.
3. **Desempenho de Tempo Real**: Orçamento de processamento mantido abaixo de **8 ms por chunk**, alcançado através de amostragem em grade espaçada (*coarse-grid trilinear interpolation*) nas funções de ruído 3D e detecção antecipada de colunas e seções vazias (*section occupancy culling*).

---

## 2. Pipeline de Geração (Passos 1 a 4 por Chunk)

```
[ ChunkGenerationScheduler ] (Background Worker Thread)
           │
           ▼
┌─────────────────────────────────────────────────────────────────┐
│ 1. Pass 1: Análise de Colunas e Preenchimento de Voxels        │
│    • Amostragem Climática (5 eixos via ClimateConfig)           │
│    • Seleção de Bioma (BiomeRegistry)                           │
│    • Blending de Biomas & Cálculo de Inclinação (|∇h|)          │
│    • Regras de Superfície (Neve, Penhasco, Praia, Subsuperfície) │
│    • Camada de Bedrock Variável                                 │
│    • Modo Caos (Densidade 3D com interpolação trilinear)       │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. Pass 2: Escavação de Cavernas 3D (CaveConfig)                │
│    • Cheese Caverns (grandes salões abertos)                   │
│    • Spaghetti Tunnels (túneis sinuosos conectados)             │
│    • Noodle Tunnels (galerias estreitas auxiliares)             │
│    • Amostragem em grade grosseira (coarseGridStep = 4)         │
│    • Proteção de Superfície, Corpos d'Água e Teto do Bedrock    │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. Pass 3: Distribuição Determinística de Minérios             │
│    • Varredura de 27 vizinhos (3×3 chunks)                     │
│    • PRNG com hash determinístico por (chunk, ore)              │
│    • Filtros de Bioma e Curva de Probabilidade por Altura (Y)   │
│    • Elipsóides com rotação, inclinação e máscara de substituição│
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. Pass 4: Estruturas Procedurais & Árvores (StructurePlacer)   │
│    • Ordenação alfabética das features (_preparedFeatures)      │
│    • SurfaceSampler simétrico entre chunks (sem corte de copas) │
│    • Verificação de raio de exclusão e inclinação do solo       │
│    • Estampagem de TreeRecipes proceduralmente                  │
└─────────────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│ Finalização: RecomputeSectionOccupancy() & IsDirty = true       │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. Detalhamento dos Componentes

### 3.1. Clima e Superfície (`ClimateConfig` & `OverworldGenerator`)
* **5 Eixos Climáticos**:
  1. *Continentalness*: Separa oceano profundo, litoral, planícies e terras altas.
  2. *Erosion*: Controla o relevo, suavizando áreas planas ou erguendo picos escarpados.
  3. *Peaks & Valleys (Ridged)*: Cristas montanhosas dramáticas.
  4. *Temperature*: Diferencia biomas quentes (Desertos/Dunas) de frios (Montanhas nevadas).
  5. *Humidity*: Diferencia florestas densas de planícies e pradarias.
* **Cálculo de Inclinação Local (`ComputeLocalSlope`)**:
  Calcula o gradiente topográfico $|∇h|$ com baseline de 2 blocos combinado ao `macroSlope` entre biomas. Evita que árvores nasçam em penhascos verticais e aplica automaticamente blocos de penhasco (`steepSlopeBlock`).
* **Proteção de Água e Praias**:
  - `IsBeachColumn`: Produz praias de areia suavemente integradas com transição por ruído.
  - Regra absoluta: **Nunca gera grama debaixo d'água**; colunas submersas recebem terra ou areia.

---

### 3.2. Modo Caos e Ilhas Flutuantes (`ChaosConfig`)
* Ativado por máscara 2D de baixa frequência (`maskThreshold`).
* Emprega função de densidade tridimensional:
  $$\text{Density} = -(wy - \text{surfaceY}) \cdot \text{densityGradient} + (\text{Noise}_{3D} - 0.5) \cdot \text{densityAmplitude}$$
* **Otimização de Desempenho**: As avaliações de ruído 3D são realizadas em uma grade grosseira delimitada ao intervalo vertical ativo ($[surfaceY - 40, surfaceY + 30]$), interpoladas trilinearmente com ganho de velocidade de até 64×.

---

### 3.3. Cavernas Multicamadas (`CaveConfig`)
* **Três Tipos de Galerias**:
  1. **Cheese Caverns**: Zonas onde o ruído 3D ultrapassa `cheeseThreshold` modularizado por profundidade.
  2. **Spaghetti Tunnels**: Intersecção onde dois ruídos 3D independentes estão próximos de zero simultaneamente ($|S_1 - 0.5| < T$ e $|S_2 - 0.5| < T$).
  3. **Noodle Tunnels**: Túneis estreitos para enriquecer a interconectividade.
* **Otimização Coarse-Grid**:
  - Avalia o ruído 3D apenas a cada $S$ blocos (padrão `coarseGridStep = 4`).
  - Redução numérica de avaliações: de $\sim 490.000$ chamadas de ruído por chunk para $\sim 5.250$ chamadas (**$\approx 98.9\%$ de redução**).
  - Alinhamento de borda sem costuras: $originX + gx \cdot S$ bate exatamente com o ponto inicial do chunk adjacente.
  - Early-exit: Se o teto máximo da caverna no chunk for inferior ao limite de proteção do bedrock, o corte é abortado com 0 ruídos avaliados.

---

### 3.4. Minérios Determinísticos (`OreDefinition`)
* **Estampagem de Vizinhança (27 Chunks)**:
  Para que veios que cruzam as bordas de um chunk não sejam cortados, o gerador processa todas as tentativas de origem nos chunks vizinhos ($[-1, +1]$) projetando os elipsóides dentro do volume local.
* **Filtro Climático de Origem**:
  Antes de iniciar o veio, o gerador valida `ore.IsBiomeAllowed(veinBiome)`. Minérios exclusivos (ex.: ouro de montanha) não vazam para outros biomas.

---

### 3.5. Estruturas e Árvores (`StructurePlacer` & `TreeRecipe`)
* **Determinismo de Features**:
  `OverworldGenerator.Prepare()` ordena alfabeticamente os assets registrados em `_preparedFeatures`, eliminando qualquer desordem de hash do runtime.
* **Amostragem Simétrica (`SurfaceSampler`)**:
  Elimina o problema de árvores partidas na divisa de chunk. Tanto o chunk do tronco quanto o chunk vizinho da copa consultam o relevo através do `SurfaceSampler`, garantindo uma decisão idêntica de geração.

---

## 4. Renderização e Shaders

* **Shader URP de Voxel**: [`Assets/Shaders/VoxelChunk.shader`](file:///c:/Users/Usuario/WorkSpace/GridBasedSandbox/GridBasedSandbox/Assets/Shaders/VoxelChunk.shader)
  - Desenvolvido especificamente para o Universal Render Pipeline (URP).
  - Suporta cor dos vértices (`COLOR`) com a keyword `_VERTEX_COLOR`.
  - Multiplica textura do atlas por cores de placeholder e tinturas de bioma.
  - Iluminação direcional com atenuação de sombras, ambient esférico harmônico e fog URP.
* **Material**: [`VoxelChunkMaterial.mat`](file:///c:/Users/Usuario/WorkSpace/GridBasedSandbox/GridBasedSandbox/Assets/Materials/Block%20Atlas/VoxelChunkMaterial.mat) com `_VERTEX_COLOR` ativo.

---

## 5. Ferramentas de Verificação e Testes

### 5.1. Janela de Teste de Determinismo (`DeterminismVerifier`)
Disponível no editor em:
* **Menu**: `GridBasedSandbox > Terrain Determinism Verifier`
* **Menu**: `Tools > Voxel World > Verify Determinism`

#### O Que o Teste Faz:
1. **Passo Sequencial**: Gera uma grade de $N \times N$ chunks (ex.: 3×3 = 9 chunks ou 4×4 = 16 chunks) na ordem padrão e calcula o hash MD5 dos blocos de cada chunk.
2. **Passo Embaralhado**: Gera exatamente a mesma grade em ordem reversa/embaralhada em novos buffers.
3. **Comparação Criptográfica**: Compara os hashes MD5 chunk por chunk.
   - **Resultado Esperado**: `100% Deterministic (All hashes matched)`.
   - Se houver divergência, aponta exatamente a coordenada local, coordenada de mundo e os IDs dos blocos divergentes.
4. **Sensibilidade de Semente**: Gera o chunk central com uma semente diferente e valida que o terreno é sensível à semente (eliminando falsos positivos).

---

## 6. Guia Rápido de Configuração de Biomas

Para criar um novo bioma:
1. Clique com o botão direito no Project: **Create > VoxelWorld > Biome Definition**.
2. Configure as propriedades climáticas:
   - `Continentalness`, `Erosion`, `Temperature`, `Humidity`.
3. Defina os blocos:
   - `surfaceBlock` (ex.: Grama)
   - `subsurfaceBlock` (ex.: Terra)
   - `steepSlopeBlock` (ex.: Rocha/Stone para penhascos)
   - `beachBlock` (ex.: Areia)
   - `snowlineBlock` (ex.: Neve para altitudes acima de `snowlineY`)
4. Ajuste as árvores e estruturas na lista `features`.
5. Adicione o bioma ao asset `BiomeRegistry`.
