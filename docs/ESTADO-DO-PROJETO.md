# Estado do projeto

Atualizado em 28/10/2026, na passagem do Mac (MacBook Pro M4 Max) para um computador com Windows. Este documento diz o que é o
jogo, o que já existe, o que está decidido, o que falta decidir e como continuar. Os detalhes estão nos documentos que ele indica.

## O jogo

Terror e furtividade em primeira pessoa numa ilha onde um projeto de pesquisa com dinossauros deu errado. O jogador quase não
consegue enfrentar os dinossauros: ele se esconde, espera o momento certo, anda sem fazer barulho e arremessa objetos para
distrair. O clima principal é a **noite**, com o jogador carregando só uma **lanterna**, que deixa ver mas também denuncia.

Decisões do dono, em ordem:

| Data | Decisão |
|---|---|
| 13/10 | Engine Unity (HDRP), câmera em primeira pessoa, referência *Alien: Isolation* |
| 13/10 | Bioma base: floresta temperada de abetos e samambaias com neblina (não há vegetação tropical CC0 de qualidade) |
| 15/10 | Floresta muito densa, para o dinossauro surgir de repente entre as árvores |
| 15/10 | A noite com lanterna é o foco do jogo |
| 15/10 | Vento na folhagem e vegetação que reage a corpos, antes do mapa e dos dinossauros |
| 17/10 | Sombras do luar mais curtas à noite para pagar o vento; galhos que cruzam um corpo passam por ele (opção a) |
| 21/10 | Demo de 20 a 30 min; jogo completo de 3 a 5 h |
| 21/10 | Premissa: um projeto de pesquisa que deu errado |
| 21/10 | Estrutura: **mundo aberto com objetivos** |
| 21/10 | Espécies famosas, nada aquático; uma primeira ameaça mais leve que os raptors |

Cuidado jurídico: os nomes das espécies são livres, mas os designs e invenções de *Jurassic Park* são da Universal (o
Dilophosaurus com babado que cospe veneno, Isla Nublar, InGen, logos). O jogo usa designs e nomes próprios. Modelos que são
réplicas dos filmes servem só como provisórios.

## O que existe no repositório

Unity **6000.6.0f1**, HDRP 17.6, Input System, AI Navigation. Tudo integrado ao `main` pelos PRs #1, #2 e #3.

| Parte | Onde | Estado |
|---|---|---|
| Protótipo cinza (o loop de furtividade) | cena `Greybox.unity`, gerada por `Assets/Game/Editor/GreyboxSceneBuilder.cs` (menu **Gamex > Rebuild Greybox Scene**) | Um raptor que patrulha, investiga ruído, persegue e procura; esconderijos; pedras para arremessar; rádio de resgate. 7 testes |
| Floresta | código em `Assets/Game/Editor/Forest`, cena `Forest.unity` gerada pelo menu **Gamex > Forest > Build All** | Mata densa de abetos e samambaias com neblina, só com scans CC0 do Poly Haven. Cerca de 280 abetos altos e 660 jovens por hectare na área jogável de 1,9 ha |
| Noite e lanterna | `ForestLightingPresets.cs`, `Assets/Game/Scripts/Player/PlayerFlashlight.cs` | Noite é o padrão da cena; lanterna de 300 lm, tecla **F** ou ombro esquerdo do controle. Modo de dia: **Gamex > Forest > Preset > Overcast Mist** |
| Vento e interação | `Assets/Game/Shaders`, `Assets/Game/Scripts/World/Foliage` | Vento com rajadas e inércia; samambaias e abetos jovens abrem passagem para corpos (`FoliagePusher`); ganchos para som de folhas e ruído de furtividade |
| Testes | `Assets/Game/Tests` | Forest EditMode 56, Forest PlayMode 24 (mais 11 de vento, rodados com filtro), Greybox PlayMode 7. Todos passando em 19/10 |
| Ferramentas | `Tools/forest` | Download dos assets por manifesto (`fetch_assets.py`, confere tamanho e md5) e medição de FBX (`fbx_objects.py`) |

Desempenho medido num build de desenvolvimento para macOS, M4 Max, 2560 x 1440, lanterna ligada, Mac ocioso: pior vista
**10,45 ms** por quadro (média em janelas de 16 quadros), contra a meta de **12,5 ms**. No Windows os números serão outros e
precisam ser medidos de novo.

**Não está no Git, de propósito:** os assets baixados do Poly Haven (cerca de 1,5 GB) e tudo o que é gerado a partir deles (a
cena `Forest.unity`, `ForestKit`, `ForestData`). Vários arquivos passam do limite de 100 MB do GitHub. Eles são recriados pelos
passos abaixo.

## Como montar no Windows

1. Instalar o Unity Hub, **entrar com a conta da Unity** (sem isso a Unity não roda pela linha de comando: sai com código 198) e
   instalar exatamente a versão **6000.6.0f1**.
2. Instalar Python 3 (o `curl` já vem no Windows 10 e 11).
3. Clonar o repositório e abrir o projeto pelo Hub. A primeira importação demora (shaders do HDRP).
4. Baixar os assets da floresta, a partir da pasta do projeto:
   ```
   python Tools/forest/fetch_assets.py Assets/Game/Editor/Forest/forest-asset-manifest.json . --milestone M4
   python Tools/forest/fetch_assets.py Assets/Game/Editor/Forest/forest-asset-manifest.json . --id qwantani_night_puresky
   ```
5. Na Unity: **Gamex > Forest > Build All** (alguns minutos). Depois abrir `Assets/Game/Scenes/Forest.unity` e dar Play.
6. Testes: **Window > General > Test Runner**, ou pela linha de comando (comandos no `CLAUDE.md`).

Nada disso foi testado no Windows ainda. O que pode dar diferença: caminhos nas ferramentas Python, o comportamento do HDRP no
Direct3D (o projeto foi feito em Metal) e o assistente do HDRP, que pode querer ajustar configurações ao abrir.

## Decisões em aberto

### 1. Unity ou Unreal (antes de começar a demo)

Dois fatos levantaram a dúvida em 22/10:
- A Unity colocou o **HDRP em modo de manutenção**: continua funcionando, mas sem recursos novos
  ([estratégia de 2026](https://unity.com/topics/render-pipelines-strategy-for-2026)).
- Os pontos fracos atuais (árvores à média distância, luz chapada sob as copas, mundo aberto) são justamente os pontos fortes da
  Unreal 5: Nanite, Lumen, World Partition, PCG e percepção de IA pronta. Lá também há muito mais floresta pronta para comprar no
  Fab.

O contra: o Claude trabalha pior na Unreal (Blueprints e assets são binários), e o projeto atual seria refeito.

Plano combinado: um **teste comparativo de 2 a 4 dias** antes da demo. Reconstruir a mesma floresta noturna com lanterna na
Unreal 5 com Nanite e Lumen, usando assets gratuitos: a demo do [Pine Forest Pack](https://www.fab.com/listings/e19f63fc-a62e-47c8-9707-999e00991c27),
o Project Titan e o Electric Dreams da Epic. Comparar visual nas mesmas vistas, tempo por quadro e quanto o Claude consegue
fazer sozinho. **O Windows é a plataforma principal da Unreal**, então o teste fica mais justo no computador novo. Comprar
packs de floresta só se a migração for decidida.

### 2. As decisões do documento da ilha

Estão na seção 9 de [`design/ilha.md`](design/ilha.md). As seis primeiras travam a demo:
1. História e protagonista (três nomes possíveis; recomendado: "Quarentena", Ilha Fallow, empresa Pellbrook, projeto ATAVUS, com
   um pescador náufrago).
2. Cenário e plantas: ilha fria vulcânica com centro geotérmico; savana de samambaias; teste de ~US$ 40 com plantas compradas.
3. Para quem é a demo (pública na Steam, recomendada; apresentação; ou interna) e quantas horas por semana o dono pode dar.
4. Conteúdo da demo: raptors só vistos pela janela (recomendado) ou um encontro jogável curto.
5. Raptors com penas (Deinonychus, recomendado) ou com escamas.
6. Teto de desempenho e o que cortar se a floresta densa não couber com os dinossauros.

As decisões 7 a 11 (áudio, regras de jogo, tamanho, final, itens menores) podem esperar.

## Próximos passos sugeridos

1. Montar o projeto no Windows e conferir que a floresta, a noite e os testes funcionam lá.
2. Fazer o teste comparativo Unity × Unreal e decidir a engine.
3. Responder as seis decisões da ilha.
4. Começar a demo (zona 1 sobre a floresta existente) e comprar os três modelos de dinossauro da demo.

## Onde está cada coisa

| Documento | O que tem |
|---|---|
| [`design/ilha.md`](design/ilha.md) | Documento de design da ilha inteira (versão 2): biomas, espécies, locais, objetivos, furtividade, técnica, produção, decisões |
| [`design/ilha-mapa.html`](design/ilha-mapa.html) | Mapa interativo da ilha (abrir no navegador). Dados em `design/ilha-mapa.json` |
| [`design/ilha-trabalho/`](design/ilha-trabalho/) | As quatro pesquisas e as três revisões que embasam o documento da ilha |
| [`floresta/spec.md`](floresta/spec.md) | Especificação original da floresta (versão 1, marcos M0 a M4) |
| [`floresta/build/STATE.md`](floresta/build/STATE.md) | Histórico da construção da floresta, etapa por etapa |
| [`floresta/build/issues/`](floresta/build/issues/) | Cada desvio da especificação, com o motivo e as medições (densidade D2, noite N1, vento W1) |
| [`floresta/build/W1-spec.md`](floresta/build/W1-spec.md) e `BRIEF-*.md` | Especificação do vento e os briefs das etapas de densidade, noite e vento |
| [`floresta/research/`](floresta/research/) | Pesquisas que embasaram a floresta: assets CC0, importação, iluminação, terreno, verificação |
| [`../CLAUDE.md`](../CLAUDE.md) | Instruções para o Claude Code neste repositório |

Os registros da floresta citam caminhos do Mac antigo (`/private/tmp/...`, `Logs/forest-work/...`, `B/...`). Eles apontam para
arquivos de trabalho, capturas e logs que ficaram lá e não foram trazidos. As decisões e os números estão nos próprios textos.
