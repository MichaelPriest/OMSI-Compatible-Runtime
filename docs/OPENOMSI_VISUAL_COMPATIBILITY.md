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
