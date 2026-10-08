# Compatibilidade visual openOMSI -> OMSI-Compatible-Runtime

Referência: [openOMSI](https://github.com/openOMSI-Project/openOMSI), licença MIT (Copyright © 2026 usonskyyyy).
Os comportamentos documentados aqui foram conferidos na árvore `main` do openOMSI em 2026-10-08.
Este documento **não** significa que a compatibilidade de visualização é 100% — os resultados visuais devem ser verificados no Windows com instalações e add-ons reais do OMSI 2.

## Componentes já integrados / melhorados

| Componente | Fonte de referência | Estado no Runtime |
| --- | --- | --- |
| O3D v1/v2 e v3 com flags e contagens de 32 bits | `crates/omsi-o3d/src/lib.rs` | Parser C# atualizado; teste de formato v3 |
| O3D v4+ / vertices protegidos | `crates/omsi-o3d/src/lib.rs` | Decoder já existente; tolerância a tags desconhecidas e triângulos inválidos aprimorada |
| `.x` texto | `crates/omsi-o3d/src/xfile.rs` | Loader próprio existente; equivalência não certificada |
| Múltiplos `[matl]` do mesmo slot | `crates/omsi-app/src/scene/vehicle_materials.rs` | Overrides cumulativos, último alpha explícito |
| `[matl_noZcheck]` | `crates/omsi-app/src/scene/vehicle_materials.rs` | Depth test mantido por padrão; legado opt-in via `OMSI_NOZCHECK_BIAS=1` |
| Texturas dinâmicas `[matl_freetex]` | `crates/omsi-app/src/scene/vehicle_materials.rs` | Resolução da parte após `Texture/` em caminhos de scripts |
| Partes acopladas e índices de texto | `crates/omsi-app/src/scene/vehicles.rs` | Mantém referências da seção principal onde não há declaração local |
| Base do solo | `crates/omsi-app/src/scene/upload.rs` | `Texture/gras.bmp` como fallback original quando disponível |
| Texturas de add-ons relativas a `Splines/` e `Sceneryobjects/` | `crates/omsi-texture/src/lib.rs` (`find_texture_in_season` e `find_texture_in_dir`) | Resolve caminhos com prefixo de categoria OMSI e mantém escopo, fallback de extensão e prioridade DDS local; smoke tests adicionados. |
| Carregamento incremental de texturas de mapa | `crates/omsi-app/src/scene/upload.rs` | D3D11 por lotes já existente; coleta estendida a materiais alternativos |
| Prévia 3D do launcher | `crates/omsi-app/src/launcher/showroom.rs` | Captura D3D11 local e cache PNG; validar posições, texturas e add-ons reais |

## Lacunas relevantes antes de chamar de 100%

1. **Texturas escritas por scripts**: `[scripttexture]`, `[useScriptTexture]`, displays tipo matriz e todas as variantes de `[scriptshare]` ainda precisam de uma implementação e verificação completa (não confundir com `[texttexture]`).
2. **Endereçamento de materiais**: `[matl_texadress_*]` (wrap/clamp/mirror/border), camadas de lightmaps múltiplas, gradientes, night textures, envmaps e configuração sazonal têm diferenças a medir contra o openOMSI.
3. **Modelos**: testar protótipos v3/v4/v7 protegidos, meshes com transform, rodas, portas, painéis, skinning, LoD, freetex e `[matl_change]` — sem gerar geometria fictícia.
4. **Mapas**: verificar `global.cfg`, tiles, terreno, `[groundtex]`, máscaras, lightmap, splines, junctions, objetos com texturas noturnas, caminhos de textura em add-ons e streaming sob pressão da GPU.
5. **Mecânica**: comandos OMSI, `M_Wheel`, `Brakeforce`, estado de câmbio, freio de estacionamento, scripts dos veículos articulados e ODE precisam de testes reais com diferentes ônibus.
6. **Renderizador**: diferenças arquiteturais entre wgpu/openOMSI e D3D11/Runtime impedem copiar o sistema gráfico inteiro como um arquivo; requer portar e validar o comportamento recurso por recurso.

## Aceite prático da biblioteca instalada

- Abrir um mapa stock e outro add-on. Confirmar texturas base/detail/mask/night e sinais sem sumir durante o streaming.
- Abrir MAN stock, veículo com painel de matriz, ônibus com repaint e articulado. Conferir painel estável, animações, rodas, portas, retrovisores e materiais transparentes.
- Gerar uma miniatura 3D por ônibus e repetir o lançamento: imagem corresponde ao veículo instalado e o cache é reutilizado.
- Ligar o motor, selecionar D, liberar freio de estacionamento, acelerar e comparar `vehicle-physics-state.log` contra o movimento e estado do painel.
- Coletar `vehicle-panel-state.log`, `vehicle-runtime-state.log`, `vehicle-texture*.log` (se presente), `transmap-missing.log`, captura de tela e nome de mapa/veículo para eventuais divergências.
- Manter smoke tests do repositório passando; **não** substituir teste real do OMSI com mocks.

Referência de termos e licença: [openOMSI / LICENSE](https://github.com/openOMSI-Project/openOMSI/blob/main/LICENSE).

## ESC: navegação opcional, sem alterar a essência do OMSI

A entrada `ESC` agora abre e fecha o menu OMSI do Runtime (a tecla Alt
permanece compatível). Foram acrescentadas ações diretas para:

- Mini mapa NavPulse (ativo/inativo)
- Mapa ampliado (abre ou fecha a visualização existente)
- LiveBoard (ativo/inativo)
- Setas sobre o percurso, estilo Forza (ativo/inativo, **desligado por padrão**)

As preferências de mini mapa, LiveBoard e setas ficam em
`%LOCALAPPDATA%/OMSI-Compatible-Runtime/navigation-overlays.json`.
O sistema usa a rota real recebida pelo `WorldNavigationAssist` para
gerar a geometria 3D existente, próxima da superfície da via; quando
não há rota, não desenha setas fictícias. A geometria e seus eixos ainda
precisam ser testados com asfalto, bifurcações e pontes de mapas reais.

**Ainda não é paridade total com openOMSI:** o mapa NavPulse atual é uma
visualização própria do Runtime. Zoom no cursor, deslocamento livre,
rede de faixas validada e navegação recalculada do `Navigator` Rust
precisam ser adaptados e validados em etapas; não estão declarados
concluídos só por essas opções aparecerem no ESC.


## Forza-inspired surface-aligned route chevrons

The ground guide now uses short cyan V-shaped chevrons, a narrow bright center,
subtle outer glow, and close 5.5 m spacing (up to 175 m ahead).
Unlike the original long floating arrows, each stroke vertex is projected
onto the **actual streamed spline/scenery road triangles**, preserving road
camber/grade while rejecting bridges with mismatched vertical level. The
decals retain depth testing, sit a few centimeters above the asphalt, and
only appear for an active OMSI route. On leaving the route they turn orange.
They are disabled by default and controlled through the ESC menu.

This is an independently drawn **Forza-inspired visual**, not a copied Forza
asset, shader or a claim of pixel-exact reproduction. The dedicated CI
geometry smoke test covers sloped surfaces, missing meshes and overpasses.
Real-world add-on testing is still needed for junctions, multi-level roads,
and render transparency under night lighting.


## Regression pass — buses / IBIS / AI / map texture assets

- **Engine startup:** an OMSI local variable is authoritative only when
  its VM writes it. If `engine_on` is merely declared, the host EngineStart/
  EngineOff controls keep working. Actual scripts still control their engine.
- **Unscheduled AI:** valid terminal OMSI roads can spawn real catalogued
  vehicles; vehicles recycle upon reaching their terminal. Zero AI when
  there is no valid real .bus/.ovh asset remains a correct diagnostic, not a
  reason to spawn fake vehicles.
- **Vehicle/dashboard:** scoped lookup includes vehicle Texture/IBIS and
  shared pack folders, with optimized DDS support for OMSI fonts.
- **Road/scenery:** root and asset-category Texture folders are checked as
  ordered fallbacks. No files are synthesized; existing texture decoding
  diagnostics are still relevant for unsupported formats.
- **Unverified:** precise NG IBIS textures, actual engine movement, and local
  installed map scenery still require the specific OMSI assets and runtime
  logs from the reported installation. CI uses non-proprietary fixtures.

## Mapa/splines — travadas ao entrar na próxima tile

Ao trocar malhas em streaming, `ReplacePreparedDrivingSurfaces` agora
atualiza os samplers sem ressituar/sincronizar o ônibus em movimento com
ODE. A posição/velocidade e o estado articulado devem continuar sendo
integrados pelo próprio corpo físico. Só permite a correção inicial quando
o veículo está parado e a suspensão ODE ainda não possui contato.

Objetos OMSI sem `[surface]`, mas com `[boundingbox]` extremamente
plana (até 0,35 m de altura), não recebem uma parede 3D substituta.
Meshes de colisão explícitos continuam valendo. O objetivo é não bloquear
a transição de splines por placas de asfalto/crossings do cenário.

## ESC → Rota e destino

O comando que já existia no menu ESC, mas estava desativado, abre um
seletor pesquisável da **lista de rotas reais resolvidas**, contendo
linha, track e destino da tabela OMSI. Escolher define imediatamente
os campos operacionais do RouteCore, que alimentam a navegação 3D
existente na próxima atualização. Rotas sem arquivo de timetable/track
não são inventadas. O IBIS físico ainda depende da lógica real do
script e de seu teclado, não é tratado como já sincronizado com o
seletor de GPS.


## openOMSI navigator: mapa livre / zoom / veículos reais

Paridade incremental com o conceito de `navigator.rs` e
`launcher/mapview.rs` do openOMSI, implementada com desenho original
em WinForms sobre o D3D11 (sem copiar código ou assets de terceiros):

- **Mapa completo**: arrastar com mouse para mover, roda do mouse para
  zoom ancorado no cursor e controles +/-.
- **Seguir ônibus / livre**: alterna entre acompanhar o veículo e
  inspecionar uma região sem retornar automaticamente à posição do jogador.
- **Norte / direção**: alterna entre mapa fixo com norte para cima
  e mapa orientado pelo ônibus.
- **IA ON / OFF**: desenha os veículos reais da simulação com orientação
  e cor distinta dos jogadores de sessão. Sem veículos fictícios.
- **Streaming**: quando entram novas tiles, as ruas do mapa são
  reconstruídas a partir das splines carregadas, em vez de permanecerem
  presas à região inicial.
- Preferências: `MapNorthUp`, `MapShowTraffic`,
  `MapFollowVehicle`, `FullMapRangeMeters` salvas no mesmo arquivo
  dos overlays; mini mapa e Live Board continuam opcionais.
- Testes CI de projeção X/Z para norte/direção e invariantes de pan/zoom
  sobre a posição do cursor.

A rede de faixas completa, as paradas rotuladas e o reroute automático
por Dijkstra do openOMSI ainda **não** foram integrados nesta etapa.


## openOMSI-inspired map: real road traffic paths, congestion and stops

The optional navigator now draws the active loaded OMSI **road-vehicle
[path] network** rather than treating every visual spline as a traffic
lane. It includes mapped road objects and junctions; pedestrian paths,
tram rails and aircraft routes are excluded. Maps with no [path]
metadata retain a spline-based fallback. A 240 m spatial grid limits
the per-frame drawing work, even when the whole map is loaded.

The map colours affected road paths amber/red where at least two
**actual** simulated AI vehicles are slow relative to their road
speed limit. A single stationary car at a red light is not marked as
a traffic jam, and congestion levels are smoothed to avoid flicker.

The selected real timetable route contributes its authored stop names
and distance along the route. The corresponding map markers are
interpolated on the route's own road geometry; the final stop has a
different colour. Stop display has an optional toggle, and is saved
with the other navigator options.

The map's network coverage remains scoped to **loaded map windows**:
whole-map background road discovery and full Dijkstra rerouting are
still separate future increments. CI smoke covers road-type selection,
spatial indexing, live AI congestion and authored stop placement.
