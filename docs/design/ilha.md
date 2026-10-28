# Ilha: documento de design

Versão 2, 2026-10-22. Este documento é a fonte que as próximas etapas de construção seguem. A versão 1 (2026-10-21) juntou quatro
pesquisas (`docs/design/ilha-trabalho/research/biomes.md`, `species.md`, `open-world.md`, `facility.md`) e os registros da floresta
(`docs/floresta/`). A versão 2 incorpora as três revisões de 2026-10-22: escopo, terror e técnica
(`docs/design/ilha-trabalho/reviews/scope.md`, `horror.md`, `technical.md`). O mapa vetorial está em `docs/design/ilha-mapa.json`
(versão 2), e todas as posições e áreas citadas aqui vêm dele. Quando as fontes discordavam, este documento escolhe uma opção e diz
por quê. O que foi aceito, adaptado ou recusado das revisões está em "Notas da revisão", no fim. As escolhas que só o dono pode
fazer estão na seção 9.

**Marcas usadas**

| Marca | Significado |
|---|---|
| [M] | Medido neste projeto. O registro é citado |
| [V] | Verificado numa fonte externa (link no texto ou em Fontes) |
| [E] | Estimativa. O raciocínio é dado; nada foi medido |
| [D] | Valor de projeto: ponto de partida para ajustar em teste |
| NÃO VERIFICADO | Não foi possível confirmar. Trate como risco |

**Convenções.** Coordenadas em metros, `x` = leste, `z` = norte, origem no centro da ilha (eixos do Unity). Os identificadores
técnicos ficam em inglês (`RaptorAI`, `NoiseSystem`, `HidingSpot`). **Agente-dia** é um dia de trabalho contínuo de um agente com
o dono alcançável, no regime da floresta: ela rodou 6 dias seguidos, de terça a domingo, e levou cerca de 5 dias de trabalho do
zero até a mata densa noturna com vento [M, STATE]. **Mês de calendário** = 26 agentes-dia (6 por semana) vezes um fator de 1,3 a
1,5 para as esperas pelas revisões do dono.

---

## Resumo em uma página

- **A ilha.** Uma ilha vulcânica fria do Pacífico Norte, com 1,75 x 1,29 km e 182 ha de terra, dos quais cerca de **120 ha são
  jogáveis** (118 a 128) [E]. No centro há uma caldeira de 20 ha, que é o território do T. rex. Um anel de basalto com três passos a
  cerca. O lado oeste fica a barlavento: úmido e nevoento, com a mata de abetos que já existe e grotas de samambaias. O lado leste
  fica a sotavento: mais seco, com charneca, os cercados dos herbívoros e a pista de pouso.
- **O gradiente da vegetação.** A empresa trouxe a flora "mesozoica" (samambaias arborescentes, cicas, cavalinhas, araucárias e
  ginkgos) como forragem para os herbívoros. Ela se espalhou pelo solo aquecido do campo geotérmico. Na borda da ilha a vegetação
  é nativa (abetos, charneca, capim de costa); quanto mais perto do núcleo, mais antiga ela parece. A vegetação avisa o jogador
  de que o perigo está perto.
- **Conteúdo.** 7 zonas, 8 biomas, 22 locais ativos (6 com interior completo) e 8 abrigos seguros. São 13 objetivos principais
  (3,1 a 4,3 h) e 3 opcionais (10 a 20 min), o que dá **3,3 a 4,6 h** de jogo [E]. São 5,9 km de rotas principais.
- **Estrutura: mundo aberto com objetivos.** A demo (ato 1) é guiada. Depois dela vem a vila, com duas ferramentas que podem ser
  pegas em qualquer ordem. Em seguida há três linhas independentes, também em qualquer ordem: **Energia**, **Transmissor** e
  **Código**. O chamado de rádio no cume exige as três e abre o ato final: atravessar a ilha até a pista, acendê-la e ser resgatado
  de helicóptero ao amanhecer. A tensão sobe **a cada linha concluída** (passos 2a, 2b, 3 e 4), não só por ato.
- **Espécies (7).** Compsognathus, Dilophosaurus, Deinonychus (os "raptors"), Triceratops, Parasaurolophus, Brachiosaurus e T.
  rex. Só quatro têm IA completa (compy, Dilophosaurus, raptors e T. rex); os outros três são roteirizados. Os modelos são da
  Pxltiger (6 espécies) e da DANKIE (o compy): $293,99 no total, dos quais $94,99 compram os três corpos da demo já na F0 [V].
- **Justo e assustador.** O esconderijo pode falhar (checagem com aviso), a lanterna tem três estados (Alta, Baixa, Apagada), todo
  golpe letal tem um aviso que se ouve antes, o primeiro contato letal com o T. rex, o Dilophosaurus e o Triceratops derruba em vez de
  matar, e uma morte custa uns 8 minutos de jogo na mediana (12 no pior caso).
- **Demo (20 a 30 min) sobre a floresta existente.** O jogador começa na falésia da enseada, chega à Estação de Campo, busca
  combustível no galpão da grota e volta com o galão enquanto o Dilophosaurus o espreita: esse é o clímax. Liga o gerador, salva,
  usa o rádio, sobe na torre. Fim: dois raptors cruzam o holofote do lado de fora da janela. A manada só é ouvida.
- **Técnica.** Tiles de terreno de 256 m e células de objetos de 128 m. A demo carrega Z1 inteira e o streaming fica para a F4. Os
  props pequenos sem um GameObject cada (SIS-5) entram **antes** da demo, porque Z1 na densidade da floresta teria 90 a 120 mil
  GameObjects. O teto de quadro do ambiente sai de uma medição na F0 (criaturas e escala de render), não de um número fixo.
- **Esforço e custo.** Demo: **62 a 125 agentes-dia, 3 a 7 meses**. Jogo completo: **174 a 337 agentes-dia, 9 a 19 meses** [E]. As
  taxas reais da demo substituem essas estimativas antes de qualquer compra do jogo completo (portão G-replan, seção 8.2). Compras:
  cerca de **$720 a $1.600** [E], mais designer de som e advogado, se houver.
- **Decisões do dono.** São 11, na seção 9. As seis primeiras bloqueiam a demo.

---

## 1. Visão geral

### 1.1 Premissa

Um jogo de terror e furtividade em primeira pessoa. O jogador está preso numa ilha onde um projeto de pesquisa com dinossauros deu
errado e precisa sobreviver e pedir resgate. Os dinossauros são quase imbatíveis: o jogador se esconde, espera a hora certa, anda em
silêncio e joga objetos para distrair. O clima principal é a **noite**, e a única luz é uma lanterna que mostra o caminho e também
denuncia quem a carrega.

O protótipo greybox já tem a base do jogo: um raptor (`RaptorAI`, que patrulha, investiga, persegue e procura), ruído
(`NoiseSystem`), esconderijos (`HidingSpot`), objetos para jogar (`Throwable`, ruído de 24 m) e um rádio de resgate. A morte hoje
recarrega a cena inteira (`GameSession`). A floresta já tem noite, lanterna, vento e plantas que se curvam ao redor dos corpos [M].
O projeto ainda não tem nenhum arquivo de áudio, nenhum clipe de animação e nenhum script que use `Animator` ou `AudioSource` [M,
`scope.md`].

**O propósito do projeto na ficção.** Pesquisa farmacêutica e patentes, nunca turismo. A âncora real é que o sangue de crocodilianos
é uma fonte estudada de peptídeos antimicrobianos. Uma equipe da George Mason University identificou 45 candidatos no plasma de
jacaré num estudo de cinco anos e $7,57 milhões
([NatGeo 2008](https://www.nationalgeographic.com/animals/2008/04/alligator-blood-antibiotics),
[GEN](https://genengnews.com/topics/drug-discovery/its-no-crock-that-alligator-peptides-act-as-potent-antibacterials)) [V]. Os
animais são **plantel de pesquisa**: numerados (por exemplo "DL-03") e com coleira de telemetria, não atrações.

### 1.2 Moldura da história (três opções; a A é a recomendada)

As três compartilham o propósito, a ilha fria e uma camada militar antiga. Elas diferem em quando e por que tudo deu errado.
Isso define quanto da instalação ainda funciona, e portanto quais ferramentas de luz e ritmo existem.

| | **A. Quarentena (recomendada)** | B. O abate | C. O longo silêncio |
|---|---|---|---|
| Ilha | **Fallow Island** (PT *Ilha Pousio*: terra deixada em descanso) | Morrow Island | Kelder Island |
| Empresa | **Pellbrook Life Sciences** (biotecnologia agrícola com braço farmacêutico) | Corvale Biosciences | Ostrand Biologics |
| Projeto | **ATAVUS** (latim, "ancestral") | SECOND SPRING | HINDCAST |
| O que deu errado | Há 11 dias, uma doença respiratória atingiu a equipe; os documentos a ligam a fungos nos umidificadores da incubação, não aos animais. Veio uma quarentena de emergência e a equipe saiu por mar em 30 horas. Os animais ficaram com alimentadores automáticos e cercas vivas. No dia 6, uma turbina desarmou e, sem ninguém para religá-la, as cercas caíram | Uma equipe contratada veio sacrificar os animais e apagar os dados; a chefe dos tratadores abriu os cercados | Um terremoto, falência, 3 anos de abandono, animais ferais de segunda geração |
| Estado da instalação | **Recente e silenciosa**: luzes de emergência a bateria morrendo, documentos de dias atrás, portas lacradas | Viva, desmoronando ao longo do jogo | Morta e coberta de mato |
| Distância da franquia | **A maior** | Média | A menor (lembra o Site B de *O Mundo Perdido*) |
| Custo de construção | Médio | Alto | Baixo |

Fonte: `facility.md` seção 3. Buscas na web em 2026-10-21 não acharam jogo, filme ou empresa com esses nomes. **A busca de marca
nas bases gratuitas (USPTO, INPI, EUIPO) entra na F0**, antes de qualquer nome aparecer numa captura ou num documento. Registrar
só se a demo for pública: $350 por classe nos EUA [V, fonte secundária, seção 8.4].

**Por que a A.** Ela explica todos os lugares: o visual de biossegurança, as eclusas, os chuveiros de descontaminação, o caos da
evacuação e a falta de energia. Também dá aos documentos uma voz recente e pessoal e torna o resgate difícil sem inventar um vilão:
a ilha é zona de quarentena declarada e os barcos ficam longe. A **chefe dos tratadores** se recusou a sair, e a trilha dela é a
linha C da campanha (seção 5).

**Quem é o jogador (decisão 1).** A recomendação é **um pescador cujo barco se abrigou da tempestade e bateu nas rochas**. Isso casa
com o início na falésia da enseada, com o "preso na ilha" da premissa e com um personagem sem armas. O kit dele dá a bússola desde o
primeiro minuto. As alternativas são um técnico mandado pela Pellbrook para apagar os servidores (uma virada moral), um piloto ou um
médico.

**Identidade visual da empresa.** Um logotipo só de texto e um ícone de broto de samambaia enrolado. Os prédios têm códigos ("B-07"),
os cercados têm números P ("P-02") e os animais têm IDs ("DL-03"). O logotipo não leva silhueta de dinossauro.

### 1.3 Tom

- **Terror de perseguição, não de ação.** A regra é: **ouça antes de ser visto, veja antes de ser pego, e pague por cada olhada.**
  A v1 dizia "veja antes de ser visto", mas com névoa de 30 m e lanterna isso não fecha: pelos próprios números, o predador quase
  sempre vê primeiro. O que o jogo garante é a ordem do **som**: para cada predador, o aviso de patrulha se ouve de mais longe do que
  esse predador detecta um jogador agachado com a lanterna em Baixa (tabelas 6.5 e 6.6).
- **Industrial, frio, molhado e recém-abandonado.** A base tem duas camadas. A militar dos anos 1960 tem bunkers, cabanas Quonset,
  um radome, os portões de concreto dos passos e uma pista. A da empresa, dos anos 2020, tem contêineres sobre estacas, estufas,
  cercas de malha em postes de concreto e amarelo de segurança. O precedente real é Adak, nas Aleutas: uma base naval fechada em 1997,
  com bunkers nas encostas e bairros inteiros vazios
  ([NEH](https://www.neh.gov/humanities/2017/winter/statement/adak-island-far-out-in-the-bering-sea-may-be-where-the-wind-begins),
  [US Navy BRAC](https://www.bracpmo.navy.mil/BRAC-Bases/Other-West/Former-Naval-Air-Facility-Adak/)) [V].
- **Maravilha em doses medidas**: os saurópodes e a vista dos mirantes acima da névoa.
- **Nada de parque.** Não há turistas, centro de visitantes, portão de madeira com tochas nem veículos de passeio (seção 4.9).

### 1.4 Duração

| | Demo | Jogo completo |
|---|---|---|
| Alvo do dono | 20 a 30 min | 3 a 5 h |
| Objetivos principais | 3 (O1 a O3): 23 a 30 min [E] | 13: 188 a 260 min [E] |
| Opcionais | 0 | 3 (S07, S14, S17): 10 a 20 min [E] |
| Total | 23 a 30 min | **3,3 a 4,6 h** [E] |

Os tempos supõem uma velocidade média furtiva de 0,6 a 1,0 m/s, contando esperas e desvios. Esse valor é NÃO VERIFICADO. O
registrador de partidas da demo (seção 8.5) grava posição a 1 Hz, e a mediana medida decide se a ilha cresce de volta (acima de
1,0 m/s falta conteúdo) ou se fica como está.

Depois da demo, o caminho principal soma cerca de 4,8 a 5,1 km em linha reta entre os locais, ou 6,3 a 6,6 km de rota. A 0,8 a
1,0 m/s, são 105 a 138 minutos de deslocamento, de 55 a 75 % do tempo principal depois da demo [E, `horror.md` 21]. Por isso as rotas
têm conteúdo próprio: uma vinheta a cada 150 m (seção 5.6).

### 1.5 Estrutura: mundo aberto com objetivos

O dono escolheu mundo aberto com objetivos, e a estrutura respeita essa escolha:

1. **Ato 1 (a demo) é guiado de propósito.** São O1 a O3 na zona Z1, onde o jogador aprende ruído, luz e esconderijo com pouco em
   jogo.
2. **A vila (Z2) é a porta para o resto da ilha.** Ela guarda duas ferramentas, o alicate (O4) e o cartão de nível 1 (O5), e o
   jogador pega as duas na ordem que quiser. Um par de raptors visita a vila desde o primeiro minuto depois da demo.
3. **Três linhas em qualquer ordem, que podem ser intercaladas:**
   - **A, Energia**: religar a estação de bombeamento (Z3) e depois a usina (Z4);
   - **B, Transmissor**: pegar o cartão de nível 2 e depois o módulo nos laboratórios (Z6);
   - **C, Código**: seguir a chefe dos tratadores pelos cercados até o abrigo dos saurópodes (Z5).
4. **Uma trava dura**: o chamado no cume (O12) exige as três linhas.
5. **Ato 4**: o jogador atravessa a ilha até a pista, acende as luzes e espera o resgate (O13).

**A tensão sobe por linha concluída.** O passo 2a vale depois do O3; o 2b depois da primeira linha; o 3 depois da segunda; o 4
depois do O12. Cada passo aperta o diretor e libera um comportamento novo (tabela 5.4). Assim nenhuma ordem fica plana, e a
terceira linha não concentra toda a subida no fim.

As consequências ficam no mundo. Com a energia de volta, as cercas voltam a tiquetaquear, as luzes das estradas acendem e os
terminais abrem e-mails. O O7 (ou o passo 3, o que vier primeiro) tira o T. rex da caldeira pelo Passo Leste. Se o jogador fizer a
linha A antes da C, os cercados ficam mais perigosos, e essa escolha é do jogador.

### 1.6 Como manter o ritmo de terror num mundo aberto

Num mundo aberto o designer perde o controle da ordem dos eventos: o jogador contorna sustos fixos, fica parado em terreno seguro,
volta por chão já esvaziado, chega cedo ao clímax ou se perde no escuro (`open-world.md` 3.1). Dez regras cuidam disso:

1. **A ameaça cresce pela geografia.** São quatro anéis: costa (compys), baixada (Dilophosaurus e raptors), cercados (herbívoros,
   depois o T. rex) e núcleo (T. rex e raptors). O jogador escolhe a ordem, e o mapa escolhe a intensidade. O Subnautica faz o
   mesmo com a profundidade ([GDC Vault](https://gdcvault.com/play/1025745/The-Design-of-Subnautica)).
2. **E cresce pelo progresso.** O diretor sobe de passo a cada linha concluída, em qualquer ordem (seção 5.4). Medição na F4: no
   máximo 12 minutos entre picos e nenhum passo com mais de 70 minutos.
3. **Territórios, não spawns.** Cada predador tem uma área de vida desenhada no mapa (seção 4) que se amplia por passo. O diretor
   só pode mandar uma espécie dentro da área atual dela.
4. **Um diretor e animais honestos.** Este é o desenho de *Alien: Isolation*. Um diretor sabe onde o jogador está e manda o predador
   para a **área geral**, nunca para o ponto exato; o animal tem de achar o jogador com os próprios sentidos. Depois de um pico, o
   diretor tira o animal de cena
   ([Game Developer](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation)) [V]. As regras estão na
   seção 6.8.
5. **Esconder não é garantido.** Um predador que tem um motivo (ruído, cheiro, ter visto o jogador há pouco) pode checar um
   esconderijo, com aviso e uma resposta (prender a respiração). Esconder e esperar 90 s não vence mais tudo (seção 6.1).
6. **Cada espécie é apresentada em cena num ponto de passagem obrigatória.** As cenas estão em `stagedScenes` no JSON (seção 5.8).
   Onde a ordem livre deixa uma rota pular a cena, vale a **regra do primeiro contato**: o primeiro contato letal com aquela espécie
   derruba e faz sangrar, mas não mata (seção 6.9). A sequência é: desconhecido, visto, entendido, dominado e, por fim, uma
   reviravolta.
7. **Os abrigos seguros marcam o ritmo.** São 8, e de qualquer ponto das rotas principais o abrigo mais próximo fica a no máximo
   383 m em linha reta (calculado no mapa v2). Chegar a um abrigo encerra um ciclo de tensão. Cada abrigo tem visor para a saída.
8. **Morrer custa minutos, não meia hora.** Salvamentos silenciosos a cada subpasso e em cada entrada de zona: mediana de 8 minutos
   ou menos de jogo perdido por morte (seção 6.9).
9. **O mundo muda, e as mudanças ficam.** Energia, cercas vivas, portas quebradas, a sirene parada e portões de mão única. Cada volta
   a um lugar já visitado encontra algo novo, e as rotas têm vinhetas próprias.
10. **Sempre resta um desconhecido.** O receptor só rastreia os animais com coleira; um dos raptors do bando A e todo o bando B
    nasceram soltos (tabela 6.5). O T. rex arrancou a própria coleira, que bipa "morte" numa ruína.

Além disso, **espécies de informação** deixam o mundo legível sem HUD: os chamados dos Parasaurolophus, os guinchos dos compys e as
pisadas dos saurópodes dizem ao jogador o que está por perto.

---

## 2. A ilha

### 2.1 Cenário: uma ilha vulcânica fria com um coração geotérmico

As pesquisas propuseram duas coisas que se completam. A de biomas propôs uma ilha vulcânica com um lado úmido e outro seco, como
Kauai ou Lord Howe. A de instalações propôs uma ilha fria e nevoenta do norte, como Adak. Este documento junta as duas: **uma ilha
vulcânica fria-temperada do Pacífico Norte, com um campo geotérmico no centro.**

- **Um análogo real.** Kunashir, nas Curilas, é formada por quatro vulcões ativos ligados por baixadas "com lagos e fontes termais".
  Tem floresta de coníferas e mista com abeto, espruce e pinheiro, e uma usina geotérmica de 1,8 MW
  ([Wikipedia](https://en.wikipedia.org/wiki/Kunashir_Island)) [V].
- **Por que plantas "mesozoicas" crescem numa ilha fria.** A empresa as plantou nas estufas e nas pastagens dos cercados, e elas se
  espalharam pelo solo aquecido. Na Nova Zelândia isso acontece de verdade:
  - plantas sensíveis a geada sobrevivem em áreas geotérmicas porque "o vapor e os solos aquecidos as protegem" do frio;
  - algumas espécies de clima mais quente só existem no país em sítios geotérmicos
    ([NZPCN, geothermal plants](https://nzpcn.org.nz/ecosystems/plant-communities/scrub-shrublands/geothermal/geothermal-plants)) [V];
  - a samambaia *Dicranopteris linearis*, que a pesquisa de biomas usa como análogo da savana de samambaias, está "confinada a
    sítios geotermicamente ativos" lá ([NZPCN](https://www.nzpcn.org.nz/flora/species/dicranopteris-linearis/)) [V];
  - a samambaia arborescente *Dicksonia antarctica* tem resistência RHS H3, ou seja, aguenta pouca geada
    ([RHS](https://www.rhs.org.uk/plants/5794/wd/details)) [V].

  Daí vem a regra do mapa: **a vegetação nativa fica na borda, e a mesozoica fica no núcleo e ao longo das grotas quentes.**
- **O cenário serve ao que já existe.** O kit de abetos "se lê como o Noroeste do Pacífico ou a Escandinávia" (spec da floresta,
  seção 2). O campo geotérmico também explica a usina, a pluma de vapor e as fumarolas, que mostram o vento e quebram o rastro de
  cheiro do T. rex.
- **O vento dominante vem de oés-sudoeste.** O preset da floresta sopra *para* o rumo 70 graus (`ForestWind.cs` linha 55, e
  `ForestWindGlobals.cs`: "graus para onde o vento sopra"). Portanto o lado a barlavento é o oeste [M].
- **O vento muda duas vezes na noite** (`meta.windByStep` no JSON). No início do passo 3 passa uma frente: 60 s de rajadas e o vento
  vira para vir de 200 graus. No ato 4, a calmaria do amanhecer deixa só uma brisa de terra fraca, a 40 % da força, que no lado leste
  sopra de oeste para o mar. É um parâmetro global do vento que já existe; não há chuva nem tempestade. Assim "cheiro e vento" não
  vira "deixe o T. rex a oeste". O jogador lê o vento nas plumas das fumarolas, na biruta da pista e nas partículas dentro do facho.
- **A lua fica a noroeste** (rumo 315, 50 graus de elevação, `ForestLightingPresets.cs`). Ela serve de bússola para quem repara (seção
  5.4).

**Um risco desse cenário.** Os arbustos da coleção `namaqualand` (seção 3, B6) são sul-africanos. Numa ilha fria eles precisam se
ler como arbustos sempre-verdes escuros quaisquer. Por isso as babosas e as *quiver trees* ficam de fora, exceto em solo quente. É
preciso conferir isso numa captura sob a lanterna.

### 2.2 Tamanho e forma

| Medida | Valor | Fonte |
|---|---|---|
| Contorno | 1.750 m (leste-oeste) x 1.285 m (norte-sul) | map-spec |
| Terra | 182 ha | map-spec |
| Jogável | **cerca de 120 ha** (118 a 128). Ficam de fora as falésias, as paredes internas da caldeira, a encosta norte, o que Z3 deixa fora do corredor (20,1 ha) e o promontório do farol (2,0 ha) | [E], `meta.playable` |
| Grade de tiles | 8 x 6 tiles de 256 m (x de -1024 a 1024, z de -768 a 768). 45 dos 48 tocam terra ou os 60 m de mar costeiro; 43 têm terra | map-spec |
| Caldeira | centro (130, 110); fundo com raio de 255 m (20,4 ha), a 125 m de altitude; borda até 360 m de raio | map-spec |
| Cume | (75, 420), 265 m de altitude | map-spec |
| Rotas principais | **5,9 km** em 15 rotas (R1 a R7, R8a, T1 a T5, T7). Fora delas: 1,1 km de trilhas de caça na caldeira e 0,45 km de pista. O túnel (U1), a T8 e a R8b saíram | map-spec v2 |
| Demo | zona Z1 com 15,3 ha em 5 tiles de terra; o tile T(-3,-1) é a floresta existente | map-spec |

**Por que desse tamanho** (`open-world.md` seção 1):

- Com névoa de livre percurso médio de 30 m e tudo cortado a 50 ou 55 m, o jogador percebe um disco de uns 30 m ao redor [M]. Um
  vale de 60 m fecha tanto quanto um de 600 m.
- De 3 a 5 h a 0,6 a 1,0 m/s dão de 6,5 a 18 km caminhados. Como cada trecho é percorrido 2 a 2,5 vezes, sobram **4 a 7 km de rotas
  distintas**. A pesquisa dá 120 a 150 ha (centro em uns 125 ha).
- A v1 ficava no topo dessa faixa (140 a 150 ha e 7,0 km) com a velocidade não verificada. A v2 desce para o meio: Z3 vira um
  corredor de 9,0 ha em volta da grota e da estação de bombeamento, e o farol vira luz de fundo. **O contorno não muda**: se o ritmo
  medido na demo pedir mais chão, basta reabrir essas áreas.
- Os jogos de perseguidor são pequenos: *Amnesia: The Bunker* dura uns 5,5 h dentro de um bunker
  ([Wikipedia](https://en.wikipedia.org/wiki/Amnesia:_The_Bunker)). As densidades de Far Cry dependem de veículos e não se aplicam
  aqui. Os tamanhos de mapa publicados são estimativas de fãs, NÃO VERIFICADO.

**A floresta existente na ilha.** O núcleo da floresta (130 x 150 m) fica no tile T(-3,-1), com centro em (-640, -128). A conversão
é: posição na ilha = posição local da floresta + (-640, -128). O spawn de hoje (0, -62) fica em (-640, -190). O afloramento norte
(2, 71) fica em (-638, -57) e recebe a Estação de Campo. O núcleo jogável vai de x -705 a -575 e de z -203 a -53.

**Esboço** (o norte fica em cima; o desenho exato vem do JSON):

```
                         Z4 Cumeada e Usina         Z7 cume (L1)           Z5 campo dos saurópodes      (farol L2: só fundo)
             ~~ Grota Norte ~~ S11 bomba    S12 usina ─── S13 cume ── trilha NE ── S22 abrigo
  vento ──>  Z3 corredor da grota  (S10 ruína)  [Passo Oeste]                [Passo Leste]  P-02 manada   S21 torre B
  (de OSO)   ~~ Grota Grande ~~                  ( CALDEIRA: T. rex, S14 ruína )    S20 torre  S19 controle   │ R8a
             Z1  S02 estação  B4 paliteiro          [Passo Sul] = S08 hub            TRI-2       P-03  S23 hangar
                 mata existente   S03 galpão                       Z6 S16 S15 labs  S17         S24 combustível
             início (falésia)  S04 portão  S06 depósito  S05 vila      S18
                                                  Z2         S07 porto
```

### 2.3 Zonas e como se ligam

| Zona | Nome | Anel | Área (jogável) | Biomas | Ameaça residente | Locais | Névoa no chão (livre percurso) |
|---|---|---|---|---|---|---|---|
| Z1 | Enseada do Naufrágio e Mata dos Abetos (**demo**) | costa e baixada | 15,3 ha | B1, B4, B2 (grota baixa), B7 | Compsognathus (bando 1); Dilophosaurus DL-03; raptors só no gancho final | S01 a S04 | 30 m na mata; 60 m na clareira queimada |
| Z2 | Vila e Porto | costa e baixada | 24,8 ha | B1, B8, B7, B4 | Compsognathus (bando 2); um par do bando A desde o passo 2a | S05 a S08 | 30 m na mata; 60 m na vila |
| Z3 | Grotas e Mata Alta | baixada | 29,1 ha (**9,0 ha**: o corredor) | B1, B2, B4 | Dilophosaurus DL-01 e DL-02; o bando B passa no passo 3 | S11 | 25 a 30 m |
| Z4 | Cumeada e Usina | núcleo | 13,9 ha | B5, B1, B8 | Bando B de Deinonychus (toca num tubo de lava) | S12 | 50 a 60 m; o vapor fecha a 20 m |
| Z5 | Planalto dos Cercados e Pista | cercados | 37,1 ha (35,1 sem o promontório) | B3, B6, B7, B8 | Parasaurolophus, Triceratops, Brachiosaurus; um par do bando A em S19 e S20 desde o 2a; o T. rex depois do O7 ou no passo 3 | S19 a S24 | 50 a 60 m; mais longe só no alto das torres |
| Z6 | Complexo de Pesquisa | núcleo | 16,1 ha | B8, B6, B2 (Vale Quente) | Bando A de Deinonychus (toca na Casa dos Carnívoros); compys no poço de incineração | S15 a S18 | 40 m; vapor no vale |
| Z7 | Caldeira e Cume | núcleo | 46,1 ha | B3, B4, B5, ilhas de B1 | T. rex (na bacia); bando B no cume | S13, S14 | 25 m na bacia (névoa acumulada); 50 a 60 m no anel; limpo só no cume |

**Névoa no chão: no máximo 60 m em toda a ilha.** A v1 dava 80 a 150 m ao planalto e 60 a 120 m à cumeada, o que obrigaria a
fazer impostores para as árvores além do corte de 55 m (SIS-4, 5 a 20 agentes-dia). A v2 limita o chão a 50 a 60 m, o dobro da
mata, e só a câmera no alto dos mirantes vê longe. Lá o fundo é feito de emissivos e silhuetas de baixa resolução, como na torre da
demo. O planalto ainda se lê como aberto, e a última travessia continua com pouca cobertura.

**Ligações** (todas estão em `paths`, `fences`, `oneWayGates` e `reachability` no JSON):

| De → para | Por onde | Trava |
|---|---|---|
| Z1 → Z2 | Estrada da Mata (R2) pelo **Portão da Mata** (S04) | cadeado; a chave está na caixa de chaves da Estação de Campo (fim do O3) |
| Z1 → Z3 | Trilha da Grota (T4) pela **Cerca da Grota** | alicate (O4). Na demo, parede invisível atrás de um portão real |
| Z1 → costa oeste | Trilha da Costa Oeste (T8) pela **Ponte com corrente** | **cortada**: a ponte está caída. É uma borda da demo que nunca abre |
| Z2 → hub | Estrada do Passo Sul (R3) até o bunker de comando (S08), **na boca externa do Passo Sul** | nenhuma |
| hub → Z3 → Z4 | Estrada da Usina (R6), Ramal da Bomba (R6b) | o portão do complexo da bomba pede alicate **ou** cartão 1 |
| Z4 → cume | Estrada do Cume (R7) | a porta do abrigo do cume pede o cartão 2 |
| hub → Z6 → Z5 | Estrada dos Laboratórios (R4), Estrada Leste (R5) | as portas externas dos labs pedem o cartão 1, as internas o cartão 2 |
| cume ↔ Z5 | Trilha da Cumeada Nordeste (T5) | nenhuma; é exposta |
| Z5 → pista | R5 e R8a (pista até a Torre B) | nenhuma. A R8b, até o farol, fica barrada por um deslizamento |
| caldeira | Portão do Passo Sul, no pátio do hub: **única entrada**. Travessias T6a e T6b | portões de concreto e aço de 5 m barram GG sempre. A porta de pessoal (0,9 m) do Passo Sul abre nos dois sentidos; as dos passos Oeste e Leste **só abrem de dentro** (W1, W2) |
| atalhos | W3: portão alto da usina para a R7 (de dentro, depois do O7). W4: portão de serviço leste dos labs para a R5 (de dentro, depois do O9) | mão única |

**O túnel de serviço (U1) foi adiado** para depois do lançamento. Ele custava 3 a 6 agentes-dia por um atalho de 0,95 km que era
quase do tamanho das estradas (R6 + R4 somam 1,07 km). No lugar dele, a energia do O7 acende os postes das estradas R6 e R4: a
estrada acesa é rápida de seguir e perigosa de usar (regra LV-9).

**Por que o hub mudou de lugar.** Nenhuma estrada passava a menos de 120 m de um passo da caldeira, então não havia onde apresentar o
T. rex de perto a quem passa. O bunker de comando (S08) e o abrigo 3 foram para (125, -200), com a cerca norte do pátio a 10 m do
portão do Passo Sul. As linhas B e C passam obrigatoriamente pelo hub, e a A quase sempre. Na primeira chegada, o T. rex vem até o
portão (cena CE7, seção 5.8). Na ficção, o bunker militar dos anos 1960 guardava o passo e virou o posto de comando do cercado P-01.

### 2.4 Marcos visíveis à noite

**O que a névoa permite.** Este é um cálculo sobre o preset da floresta, ainda não renderizado (`facility.md` 5.1). Abaixo de
Y = 18 m o livre percurso médio é 30 m: a 60 m resta 13,5 % do contraste, a 100 m resta 3,6 %, e a 200 m resta 0,13 %. A 45 m acima
da base da névoa o livre percurso efetivo sobe para uns 290 m, e a 60 m para uns 1.030 m. Disso saem duas regras:

- **A ilha só pode ser "lida" de mirantes acima da névoa**, e subir num deles é um ato deliberado e exposto, como as torres de
  *Breath of the Wild* ([GMTK](https://gmtk.substack.com/p/how-nintendo-solved-zeldas-open-world)).
- **Dentro da névoa, a orientação vem de pontos de luz, de som, dos caminhos, do mapa de papel e da bússola.**

**Como a névoa muda por zona (SIS-6, revisto).** Um Local Volumetric Fog só existe dentro da grade de froxels, que acaba na Volumetric
Fog Distance: 55 m no preset noturno [M]. Além disso manda a névoa global (`create-a-local-fog-effect.md`,
`fog-volume-override-reference.md`, documentação local do HDRP 17.6) [V]. Por isso a névoa de cada zona é um override **global**
(livre percurso e altura) num Volume que se mistura pela posição da câmera ao longo de 20 a 40 m. A distância da sombra da lua também
muda por zona no mesmo Volume. Os volumes locais ficam só para detalhes: névoa acumulada nas grotas e vapor. A sonda SD8 olha a
fronteira entre zonas nos dois sentidos, porque com névoa global misturada pela câmera um dos dois lados pode parecer errado.

**A assinatura de luz de cada distrito** (com base em normas reais de iluminação):

| # | Marco | Ponto | Assinatura | Base real | Muda com o jogo |
|---|---|---|---|---|---|
| L1 | Mastro do cume (75 m) e radome | (75, 425) | Vermelho, 30 lampejos por minuto, no topo e no meio | FAA L-864 ([Flight Light](https://flightlight.com/what-is-an-faa-l-864-obstruction-light/)) | Fraco (solar) no início; aceso por inteiro depois do O7. O progresso fica visível de qualquer lugar |
| L2 | Farol (só fundo) | (642, 578) | Branco, dois lampejos a cada 10 s, Fl(2) 10 s; o feixe varre a névoa | [Light characteristic](https://en.wikipedia.org/wiki/Light_characteristic) | Constante: "o mar é onde está o farol". Não se chega lá |
| L3 | Pluma de vapor da usina | (-190, 370) | Holofotes de sódio laranja por baixo; zumbido audível a 300 m ou mais [D] | | Cresce e clareia depois do O7 |
| L4 | Laboratórios | (360, -275) | Giroflex âmbar; sirene de 20 s a cada 3 min [D] | | A sirene atrai animais. Desligá-la tira um marco do mapa |
| L5 | Farol rotativo da pista | (738, -190) | Branco e verde alternados, 24 a 30 por minuto; balizamento branco | [Aerodrome beacon](https://en.wikipedia.org/wiki/Aerodrome_beacon) | Apagado até o ato 4; acendê-lo é o objetivo |
| L6 | Porto | (45, -592) | Luzes vermelha e verde; buzina de nevoeiro de 2 s a cada 30 s [D] | | Constante; a buzina é um marco sonoro terra adentro |
| L7 | Barco naufragado em chamas | (-662, -350) | Brilho laranja tremulando por baixo da névoa | | Só no ato 1 |
| L9 | Saurópodes | (570, 400) | Chamados graves; silhuetas no campo alto | | Maravilha, não orientação |
| L10 | Janelas da vila | (-250, -420) | Poucas lâmpadas quentes | | Isca: tem alguém vivo? |

**Código de cores para o jogador aprender sem tutorial:** vermelho é o cume, branco piscando é a costa, laranja é a energia, âmbar
são os laboratórios, branco e verde são a pista, e janelas quentes são a vila.

**Mirantes (9 jogáveis).** Em `vantages` no JSON. De cada um devem aparecer pelo menos 3 dos marcos L1 a L6 em direções diferentes:

| # | Mirante | Altura acima do chão |
|---|---|---|
| V1 | Torre da Estação de Campo (fim da demo) | 15 m |
| V3, V4 | Torres de Observação A e B | 18 m cada |
| V5 | Escada da torre de resfriamento | 30 m |
| V6 | Plataforma do radome | 10 m |
| V7 | Teto do bunker de comando, olhando o portão do Passo Sul | 6 m |
| V8 | Penhasco do Passo Oeste | no chão |
| V9 | Teto dos laboratórios | 9 m |
| V10 | Abrigo do Saurópode | 4 m |

A V2 (galeria do farol) saiu com o farol. As torres de 15 a 18 m não ficam acima da névoa (são precisos uns 45 m); o que elas ganham
é um Volume de câmera que abre a névoa global para 150 a 300 m enquanto a câmera está no alto, com o fundo de emissivos e silhuetas.

**Marcos sonoros.** A buzina de nevoeiro e a arrebentação marcam a costa. O zumbido da turbina marca a usina, e a sirene, os
laboratórios. Os chamados de manada vêm dos cercados, e o gerador marca a Estação de Campo depois do O3. Na névoa, o som chega mais
longe que a vista, então ele é a bússola principal dentro da mata.

**Caminhos na névoa.** As estradas têm postes de energia a cada 50 m (é só seguir os fios). Depois do O7 os postes da R6 e da R4
acendem. As placas nos cruzamentos são no estilo da empresa ("B-09 VET 400 m"). As placas retrorrefletivas, um truque de shader NÃO
VERIFICADO, ficaram para depois do lançamento.

**Mapa e orientação.** Não há mapa em tela cheia nem marcador de objetivo.

- **Mapa de papel, dado no O1** (o quadro da Estação de Campo). Ele se preenche com os lugares visitados e marca os locais citados
  nos documentos lidos. O rádio do O3 marca as três linhas por alto ("usina ao norte", "labs a leste", "cercados além dos labs").
- **Bússola** desde o primeiro minuto, do kit do pescador.
- **Quadros "você está aqui"** em cada abrigo e cruzamento principal (os quadros dos postos cortados foram para os abrigos).
- **Medição na F4**: o tempo com mais de 60 s sem progresso em direção a algum objetivo fica em 10 % da sessão ou menos.

**Sonda antes de fixar os marcos (SD9, seção 7.8).** Cartões emissivos a 50, 100, 200 e 300 m, um Lens Flare (SRP) com e sem
oclusão ambiental, um pisca vermelho atrás da copa e a câmera a Y = 0, 30, 45 e 60 m. A sonda mede o que se lê e quanto custa.
Ela também confere as poças de luz dos locais de objetivo (regra LV-3).

### 2.5 Onde o jogador começa e onde é resgatado

- **Início: a saliência da falésia da enseada (-652, -280).** O pescador se arrastou das rochas e acorda uns 15 m acima da água [D],
  com o barco queimando nas pedras 70 m ao sul (L7). Ele sobe a Trilha da Enseada (T1) até a margem sul da floresta (a `SouthBank`
  local, em (-645, -200)). A praia da enseada (S01) só é vista do alto e não é andável.
- **O mar da demo é um plano mínimo, só na enseada.** Visto de cima, a 15 a 70 m, um mar sem superfície mostraria o fundo do terreno
  iluminado pelo fogo. A solução é um plano opaco e escuro com mapa de normais em movimento, que pega o brilho do fogo e da lua. Não
  tem espuma de arrebentação nem simulação, porque o jogador nunca chega à linha d'água. Custo: 0,5 a 1 agente-dia [E], medido no
  harness. O HDRP Water System continua fora da demo.
- **Resgate: pátio da pista (780, -110), ao amanhecer.** Depois do chamado (O12), a resposta pelo rádio é: "o helicóptero pousa na
  primeira luz, se a pista estiver acesa". O jogador desce até a pista, liga o gerador do parque de combustível, acende o farol e o
  balizamento e dispara o sinalizador da plataforma (cena CE14, seção 5.8). Três razões para esse final:
  - um helicóptero não exige jogabilidade na água; um barco exigiria (`open-world.md` 4.2);
  - terminar no transmissor do cume repetiria *Trespasser* (1998) (`facility.md` 2.1);
  - a última travessia, do cume à pista (T5, T7, R8a), cruza o planalto aberto e dá ao final um tipo de tensão diferente: pouca
    cobertura e o T. rex solto.

---

## 3. Biomas

### 3.1 Regras que valem para todos

- **Plantas vivas de linhagens antigas.** Toda planta é real e ainda assim diz "era dos dinossauros": samambaia-real (*Osmunda*),
  samambaias arborescentes, cicas, cavalinhas, ginkgo e araucárias. **O chão aberto é savana de samambaias**, não pradaria de capim.
  O retrato da Formação Morrison é um mosaico de matas ciliares de coníferas e samambaias arborescentes com savanas de samambaias e
  araucárias esparsas ([Paleobiota of the Morrison Formation](https://en.wikipedia.org/wiki/Paleobiota_of_the_Morrison_Formation),
  [USGS](https://pubs.usgs.gov/publication/70027121)) [V]. Grama só aparece em volta das instalações e no capim da costa. Isso também
  conta a história: foram as pessoas que a trouxeram.
- **A lacuna do CC0.** As plantas-assinatura do Mesozoico **não existem como scans CC0**. O Poly Haven tem 521 modelos (o mesmo
  conjunto de 2026-10-13) e o ambientCG tem 34 modelos, dos quais 32 são comida [V, `biomes.md` 3]. Elas precisam ser compradas,
  quase sempre como modelos feitos à mão, e passar pelo baker de árvores e pelo afinador de folhagem que já existem. Antes de
  qualquer bioma que dependa delas vem uma **compra de teste de cerca de $40 e uma captura a um braço de distância sob a lanterna**
  (sonda E2, como o experimento E1 foi para os abetos jovens).
- **Comprar malhas e texturas, não shaders.** Todo material é refeito no Shader Graph de vento do projeto, para que vento, curvatura e
  a gradação noturna fiquem uniformes. Os shaders e as cenas de demonstração dos pacotes são ignorados.
- **Qualquer planta que esconda alguém precisa ter de 1,1 a 1,2 m.** A cápsula agachada tem 1,1 m e os olhos ficam a 0,95 m
  (`PlayerController.cs`). A `fern_02` chega a uns 0,7 m mesmo em escala 1,6, então nunca serve de esconderijo.
- **O espaçamento decide quem passa, mas conta só o que o NavMesh vê.** Os abetos jovens ficam de 2,2 a 3,2 m uns dos outros [M].
  Só que a regra de caule fino do W1 desligou a cápsula do tronco de **690 dos 1.706 abetos jovens (40 %)**, todos os de escala até
  0,705, inclusive todo o sub-bosque de chão aberto, e o bake do NavMesh só junta colisores ligados [M, W1 seção 2]. Com 60 % dos
  caules, o espaçamento sólido fica cerca de 1,3 vez maior [E]. Por isso cada tipo de agente tem a sua lista de fontes (seção 7.5):
  **os caules finos contam como obstáculo para G e GG** (animais grandes evitam moita) e não contam para M e P. A auditoria confere
  que GG não alcança nenhum ponto dos refúgios de B1 nem das ilhas de abetos da caldeira. Assim o mapa diz ao jogador onde o T. rex
  não o segue, sem paredes invisíveis.
- **Paletas de terreno.** O HDRP Terrain Lit aceita **no máximo 8 camadas por tile** (`terrain-lit-material.md`, HDRP 17.6) [V]. A v1
  dava 4 camadas por bioma; rasterizando o mapa, 17 a 31 dos 43 tiles com terra passavam de 8, entre eles três dos cinco da demo
  [`technical.md` 2]. A v2 usa **4 camadas compartilhadas pela ilha toda, mais 1 por bioma (2 na costa)**, e tira B8 do terreno
  (tabela 3.10).
- **Cada bioma é medido numa fatia própria.** A fatia tem 1 a 2 ha, passa pelo harness do player (`ForestFrameHarness`), 29 vistas e
  corridas pareadas contra a vista de calibração (seção 7.4), com lanterna, vento e pelo menos um empurrador grande ligados.

### 3.2 Visão geral

As áreas vêm do map-spec. Os alvos de visibilidade, ruído e custo são [E] de `biomes.md` (seções 5 e 6), exceto onde está marcado
[M]. Os custos de B3, B5 e B7 foram refeitos para a névoa de 50 a 60 m no chão (seção 2.3).

| # | Bioma | Área (participação) | Zonas | Linhas de visão livres além de 30 m | Ruído do chão (x raio de passo) | O T. rex passa? | Pior vista [E] | Classe de construção |
|---|---|---|---|---|---|---|---|---|
| B1 | Mata de abetos | 48,6 ha (26,6 %) | Z1, Z2, Z3, Z4, ilhas em Z7 | 8 % [M] | 0,8 a 1,3 | só por faixas de 6 m ou mais | 10,7 a 11,8 ms (estimativa pareada, seção 7.4) | **Existe** |
| B2 | Grota das samambaias | 11,2 ha (6,1 %) | Z1, Z3, Z6 | abaixo de 5 % | 0,7 a 1,6 | não | 10 a 12,5 ms | mesmo código, **kit comprado** |
| B3 | Savana de samambaias | 29,4 ha (16,1 %) | Z7, Z5 | 60 a 85 % em pé; abaixo de 15 % agachado | 0,8 a 1,3 | livre | 8,5 a 11 ms; refazer a 60 m | mesmo código, kit comprado, **cobertura do chão em escala** |
| B4 | Clareira queimada | 10,1 ha (5,6 %) | Z1, Z2, Z3, Z7 | 30 a 50 % | 1,0 a 1,6 | por faixas de 6 m | 7,5 a 9,5 ms | **quase de graça** (CC0) |
| B5 | Planalto de basalto | 28,2 ha (15,5 %) | Z7, Z4 | 70 a 90 % | 1,0 a 1,6 | sim (por fora) | 7 a 9 ms | **sistema novo** (faces de rocha) |
| B6 | Charneca de sotavento | 16,7 ha (9,1 %) | Z5, Z6 | 20 a 35 % | 1,0 a 1,3 | entre as moitas | 8 a 10 ms | **quase de graça** (CC0) |
| B7 | Costa | 27,6 ha (15,1 %) | todas | 80 a 95 % | 0,5 a 1,2 | sim, na praia | 7,5 a 9,5 ms, mais o mar | **sistemas novos** (mar, capim em escala) |
| B8 | Instalações | 10,6 ha (5,8 %) | todas | 20 a 60 % | 1,0 a 1,8 | fora dos prédios | 8 a 10 ms, mais as lâmpadas | prédios e chão por código |

O ruído do chão é um multiplicador proposto sobre o raio de ruído do passo (andar = 8 m). O jogo o lê do mapa de superfícies que o
pintor de terreno já escreve (`ForestData/SurfaceMap.png`, texels de 0,5 m). Nada lê esse mapa ainda.

**Quem paga o quê.** "Pior vista" é sempre o **ambiente**, com a definição da seção 7.4: terreno, vegetação, prédios, lâmpadas,
volumes de névoa, efeitos transparentes e água. Os números acima ainda não contam efeitos nem lâmpadas, e por isso são o piso.

### 3.3 B1 Mata de abetos (existe)

- **Onde.** A cinta de nuvens do lado de barlavento: Z1 (a demo), o entorno da vila em Z2, o corredor de Z3 e as encostas baixas de
  Z4. Dentro da caldeira há três ilhas de abetos jovens, com raios de 30 a 42 m, que servem de refúgio do T. rex.
- **À noite.** No facho aparecem troncos, ramos de abeto e samambaias, e um feixe de luz na névoa. Fora dele, pontas negras contra o
  brilho da lua numa névoa de 30 m [M].
- **Plantas.** O kit existente (coleção `pine_forest` do Poly Haven): abetos jovens e altos, `fern_02`, capim, rochas e troncos.
- **Som.** Vento nas agulhas, gotejar e galhos rangendo. O gancho de farfalhar já existe (W1).
- **Furtividade.**
  - **Visão:** do núcleo, 8 % dos raios na altura dos olhos ficam livres além de 30 m e 33 % além de 15 m; da trilha, 17 % e 47 %
    [M, D2].
  - **Cobertura:** a dura só serve agachado (rochas de musgo de 0,43 a 1,11 m e troncos de 0,1 a 0,5 m) [M, spec 7.4]. As moitas
    escondem do olho, mas não do linecast do raptor; com os volumes de ocultação (seção 6.1) elas passam a esconder também da IA.
  - **Chão:** serapilheira 1,0, agulhas secas 1,1, lama 0,8, cascalho de trilha 1,3.
  - **Lanterna:** sob a copa o luar não basta para andar, então o hábito aqui é a lanterna em **Baixa** (seção 6.6).
- **Fontes.** Já está no projeto. Para variar, há CC0: `pine_sapling_medium` e `pine_tree_01` (pesados), `fir_sapling` e
  `pine_sapling_small` (precisam do afinador), `moss_01`, `pine_roots` e `bark_debris_01`, mais `Bark014` e `Ground070`, `076` e
  `084` do ambientCG.
- **Custo.** De 10,7 a 11,8 ms na pior vista, e até 12,08 ms com empurradores [M, W1 R2]. **É uma estimativa pareada** (a leitura de
  11,51 ms do N1 mais deltas medidos), não uma leitura direta: o W1 R2 ainda deve uma confirmação de 29 vistas numa sessão normal.
  Fica de 0,4 a 1,8 ms acima do teto provisório de 10,0 ms, e as alavancas listadas somam no máximo 1,1 ms (decisão 6).
- **Densidade.** Se a decisão 6 escolher "bolsões", a densidade D2 fica só nos bolsões de furtividade (até 30 % da área de B1 [D]) e
  o resto de B1 usa uma densidade mais leve, o que também reduz os GameObjects (seção 7.1).
- **Construção.** Pronto. As bordas com B2 e B4 usam as camadas compartilhadas.

### 3.4 B2 Grota das samambaias

- **Onde.**
  - A Grota Grande desce do Passo Oeste até a costa sudoeste: a parte baixa fica em Z1 (a demo, onde vive o DL-03) e a alta no
    corredor de Z3, perto da ruína das estufas (DL-01).
  - A Grota Norte desce da usina até a costa noroeste, passando pela estação de bombeamento (DL-02).
  - O Vale Quente, em Z6, é a ravina aquecida ao lado dos laboratórios.
  - Os leitos têm de 3 a 10 m de largura e o córrego é sazonal: seco ou um fio d'água, **sem jogabilidade de água**.
- **À noite.** Um teto de guarda-chuvas de samambaias arborescentes a 3 a 8 m de altura, cicas nas margens quentes, hastes de
  cavalinha no leito, rocha molhada brilhando e névoa empoçada no fundo. A silhueta é de frondes radiais contra uma faixa de céu, bem
  diferente das pontas da B1.
- **Plantas.** Samambaias arborescentes (*Dicksonia*, *Cyathea*) no dossel. Samambaia-rei e samambaia-real (de 1 a 2 m) no sub-bosque.
  Cicas só nas margens quentes, cavalinhas no leito e musgo.
- **Som.** Gotejar, água correndo, sapos e eco curto nas paredes. É a paisagem sonora mais claustrofóbica da ilha.
- **Furtividade.**
  - **Visão:** alvo abaixo de 5 % de raios livres além de 30 m e abaixo de 10 % além de 15 m. É o bioma mais fechado.
  - **Cobertura:** está por toda parte, mas é curta.
  - **Frondes:** cada fronde que se mexe denuncia quem passa, nos dois sentidos (os eventos `Rustled` do `FoliagePusher`).
  - **Quem entra:** o T. rex não cabe no leito; o Dilophosaurus cabe. Por isso a grota é refúgio de um e armadilha do outro.
  - **Chão:** musgo molhado 0,7, lama 0,8, seixos do córrego 1,3, respingo 1,6.
- **Análogos.** Carnarvon Gorge, com cicas *Macrozamia moorei* de até uns 7 m
  ([Wikipedia](https://en.wikipedia.org/wiki/Carnarvon_Gorge)), e as grotas de samambaias de Tarra-Bulga e Lamington
  ([Commons](https://commons.wikimedia.org/wiki/Category:Tarra-Bulga_National_Park)).
- **Fontes.**
  - **CC0 de apoio:** `fern_02`, `moss_01`, `rock_moss_set_01` e `_02`, `single_root`, `root_cluster_01` e `_02`; texturas
    `mud_forest`, `river_small_rocks`, `dry_riverbed_rock`, `rock_pitted_mossy` e `mossy_rock`; e do ambientCG `Ground021` e `022`,
    `Rocks024L`, `Rock036`, `057`, `059` e `064`.
  - **Compras:**
    - BotaniCraft3D no RenderHub: [Dicksonia](https://www.renderhub.com/botanicraft3d/dicksonia-tree-plant) a $8,25 (126.279
      polígonos) e [Equisetis](https://www.renderhub.com/botanicraft3d/equisetis-plant) a $7,50 (45.801 polígonos) [V]. As páginas não
      dizem que são "prontos para jogo";
    - 3D Tree Team, [Fern Garden](https://3dtreeteam.gumroad.com/l/FernGarden), com *Cyathea* e *Dicksonia*, a $70 (NÃO VERIFICADO);
    - cicas: um pacote de *Cycas revoluta* no [Fab](https://www.fab.com/listings/33d9a24c-0413-40f5-9703-bb2a02d5bbd5), com preço não
      lido.
- **Custo [E].** De 10 a 12,5 ms, ou 0,8 a 1,1 vez o conteúdo da floresta. As vistas são curtas (uns 40 m ao longo do leito), então
  cortes a 35 ou 40 m são possíveis. As paredes da grota são os oclusores sólidos que o D1 mandou esperar: vale uma tentativa medida
  de GPU occlusion culling aqui.
- **Construção.** Mesmo código com um kit comprado. A escavação de ravina (spec 6.2, passo 5), o termo de umidade da dispersão e o
  empurrão por fronde já existem. Depende da sonda E2.

### 3.5 B3 Savana de samambaias

- **Onde.** No fundo da caldeira (Z7, 20 ha), no planalto dos cercados (Z5; as pastagens foram semeadas de samambaia e cavalinha como
  forragem) e no campo dos saurópodes (P-04).
- **À noite.** Um mar de samambaias na altura da cintura, prateado pela lua. Araucárias e ginkgos solitários se recortam contra o
  céu, e pilhas de rocha (kopjes) são ilhas escuras. O céu é a maior luz, e uma lanterna em Alta é vista de longe. **Na caldeira a
  névoa se acumula (25 m); no planalto o chão vai a 50 a 60 m**, o dobro da mata. A bacia é o lugar mais cego da ilha, e é lá que
  mora o T. rex.
- **Plantas.**
  - samambaia alta (1,2 a 2 m) cobrindo 60 a 80 % do chão;
  - cavalinhas nas baixadas úmidas;
  - araucárias e ginkgos de 10 a 30 por hectare, em grupos;
  - cicas nas elevações quentes.
- **Som.** Vento passando pelas frondes em ondas longas, chamados de herbívoros que vão longe e insetos. Silêncio perto de um
  predador é um aviso.
- **Furtividade.**
  - **Visão:** em pé, 60 a 85 % dos raios ficam livres além de 30 m. Agachado dentro da samambaia, menos de 15 %, mas cada passo
    farfalha.
  - **Pedras atiradas:** uma pedra jogada na samambaia vira a cabeça do predador para o farfalhar.
  - **Quem passa:** o T. rex anda livre, porque as árvores ficam de 18 a 30 m umas das outras.
  - **Cobertura dura:** os kopjes. Na caldeira, as três ilhas de abetos jovens (B1) são onde o T. rex não entra.
  - **Chão:** serapilheira de samambaia 0,9, solo nu 0,8, pedras 1,3.
- **Análogos.** Araucárias em chão vulcânico no
  [Conguillío](https://commons.wikimedia.org/wiki/Category:Conguill%C3%ADo_National_Park) e a samambaia uluhe havaiana
  ([Dicranopteris](https://commons.wikimedia.org/wiki/Category:Dicranopteris_linearis)).
- **Fontes.**
  - **CC0:** `namaqualand_cliff_01` (o único formato de "pilha de matacões", 8,3 x 5,0 x 4,4 m), `boulder_01`, `rock_face_01` e
    `_02` enterrados em elevações, `jacaranda_tree` (19,5 m; 3,9 M triângulos antes de afinar) e `fir_tree_01` isolado. Chão:
    `dry_decay_leaves`, `dirt`, `rocks_ground_02` e `sparse_grass`.
  - **Compras:**
    - samambaia alta: BotaniCraft3D [Bracken](https://www.renderhub.com/botanicraft3d/bracken-mature-plant) ou EVERYPlant Bracken no
      [CGTrader](https://www.cgtrader.com/3d-models/plant/other/everyplant-bracken-20-models), com preços não lidos;
    - araucária: BotaniCraft3D [Araucaria](https://www.renderhub.com/botanicraft3d/araucaria-tree) (88 a 308 mil polígonos) ou o
      [monkey puzzle](https://www.cgtrader.com/3d-models/plant/bush/araucaria-araucana-monkey-puzzle-tree) do CGTrader;
    - ginkgo: nenhum candidato conferido.
- **Custo [E].** A estimativa de 8,5 a 11 ms supunha a névoa de 30 m da mata. Com 60 m no chão, o tapete de samambaia tem de ir além
  do corte de 55 m: a 70 % de cobertura, a razão do D2 dá cerca de 3.260 touceiras por hectare. Num cone de 70 graus isso dá umas 600
  touceiras até 55 m, 720 até 60 m e 1.270 até 80 m; com a visibilidade de 120 m da v1 seriam 2.900 [E, `technical.md` 11]. Três
  medidas seguram isso:
  - o terreno debaixo do tapete é pintado no mesmo tom, para o corte a 60 a 80 m se dissolver no chão;
  - a samambaia ganha um LOD distante de cartões;
  - a sombra da lua vai a 50 a 60 m com 3 cascatas nas zonas abertas, onde há muito menos objetos que projetam sombra (SIS-6).

  Antes de comprar o kit de samambaia, uma fatia de B3 de 1 a 2 ha é medida com a névoa de 60 m. O tapete em escala é o sistema
  SIS-9 (seção 7.3), com a rota escolhida pela sonda SD16.
- **Construção.** Mesmo código de dispersão e touceiras com um kit comprado, mais a cobertura do chão em escala (SIS-9).

### 3.6 B4 Clareira queimada

- **Onde.**
  - Em Z1, entre a mata e a grota (o "paliteiro"), por onde passa a Estrada da Mata;
  - um corredor pisoteado e derrubado que atravessa a caldeira (a trilha do T. rex);
  - uma derrubada de tempestade na encosta de barlavento, em Z3 (fora do corredor, como fundo);
  - uma clareira entre a vila e o Passo Sul.
- **À noite.** Troncos mortos cinzentos e placas de raiz em silhueta. Chão negro de carvão com cinza pálida, e abetos jovens e
  samambaias rebrotando. Sob a lua, é o bioma mais gráfico: os ossos de uma floresta.
- **Som.** Troncos inclinados rangendo, vento assobiando em galhos mortos, cinza e gravetos estalando sob os pés.
- **Furtividade.**
  - **Visão:** 30 a 50 % dos raios livres além de 30 m, em corredores longos entre os troncos de pé.
  - **Cobertura:** os troncos caídos são cobertura agachado (0,3 a 1 m) e as placas de raiz são cobertura de pé (1,5 a 3 m).
  - **Chão:** é o mais barulhento da ilha. Carvão e galhos 1,4 a 1,6, cinza 1,0.
  - **Quem passa:** o T. rex atravessa por faixas de 6 m ou mais entre os troncos; vãos abaixo de 4 m o barram (seção 7.5).
- **Análogos.** [Yellowstone depois de 1988](https://commons.wikimedia.org/wiki/Category:Yellowstone_fires_of_1988) e samambaias
  arborescentes, que estão entre as primeiras plantas a se recuperar depois do fogo
  ([University of Melbourne](https://findanexpert.unimelb.edu.au/news/12942-tree-ferns-are-older-than-dinosaurs.-and-that's-not-even-the-most-interesting-thing-about-them)).
- **Fontes (quase tudo já está no projeto).**
  - **Troncos de pé:** `fir_tree_01` sem a sub-malha de galhos (a madeira tem 59.420 a 110.700 triângulos). Um terço deles pode ser o
    `fir_tree_01_c` meio morto como está. A casca carbonizada é uma variante de tinta, o mesmo mecanismo do `__tint1`.
  - **Chão de madeira:** `dead_tree_trunk` e `_02`, `dry_branches_medium_01`, `tree_stump_01` e `_02`, `bark_debris_01` e
    `root_cluster_01` escurecido.
  - **Rebrota:** `fir_sapling`, `fir_sapling_medium` e `fern_02`.
  - **Terreno:** `burned_ground_01` como camada própria; lascas de madeira e cinza como decals.
  - **Opcionais:** [Burned Wood](https://assetstore.unity.com/packages/slug/173899) ($24,99) e
    [Dead Tree Pack](https://marketplace.unity.com/packages/3d/vegetation/trees/dead-tree-pack-239578) ($26,99) [V].
- **Custo [E].** De 7,5 a 9,5 ms. Os troncos de pé são madeira opaca, sem folhagem com alpha test, então as sombras da lanterna e da
  lua saem baratas.
- **Construção.** **Quase de graça.** Precisa de uma opção no baker para descartar a folhagem, de uma regra para árvores caídas e de
  uma tinta. **É o primeiro bioma novo**, porque obriga a generalizar o contrato congelado da floresta (SIS-2).

### 3.7 B5 Planalto de basalto

- **Onde.** O anel da caldeira (com os três passos, a 140 a 165 m de altitude), o cume (265 m), as terras altas da usina em Z4 e a
  cumeada nordeste.
- **À noite.** Rocha preta e cascalho cinza sob um céu grande, abetos anões (krummholz) e vapor nas fendas. Na linha do horizonte o
  jogador vira uma silhueta contra o céu, e essa é a exposição principal.
- **Som.** Vento uivando nas cristas (mascara os passos), pedras rolando no cascalho, o silvo das fumarolas e o zumbido da usina.
- **Furtividade.**
  - **Visão:** por fora, 70 a 90 % dos raios livres além de 30 m.
  - **Cobertura:** vem do próprio terreno, em saliências e campos de blocos.
  - **Fumarolas:** são ocultação, máscara de ruído, quebra de rastro de cheiro e indicador de vento ao mesmo tempo.
  - **Chão:** o cascalho é o mais alto da ilha, 1,6.
- **Análogos.** [Hawaii Volcanoes](https://commons.wikimedia.org/wiki/Category:Hawaii_Volcanoes_National_Park) e
  [tubos de lava](https://commons.wikimedia.org/wiki/Category:Lava_tubes).
- **Fontes.**
  - **CC0:**
    - rochas: `mountainside` (153.472 triângulos no LOD0), `rock_face_01` e `_02` (abertas, então precisam ser enterradas em
      encostas), `boulder_01`, `rock_07`, `rock_09`, `stone_01` e os `namaqualand_boulder_*` recoloridos;
    - chão: `rocky_terrain_02` (compartilhada) e `lichen_rock` (própria); `rocks_ground_05`, `cliff_side`, `rock_face_03` e
      `rock_boulder_cracked`, mais `Rock031`, `033`, `035`, `047`, `048` e `Rocks006` do ambientCG, como malhas e decals;
    - plantas: `fir_sapling` e `fir_sapling_medium` reduzidos, `fern_02`, `moss_01` e `grass_medium_01`.
  - **Compras:** NatureManufacture
    [L.V.E 2023](https://marketplace.unity.com/packages/tools/terrain/l-v-e-2023-lava-volcano-environment-2023-253816), $70, HDRP até a
    6000.3, de março de 2026; o conteúdo é NÃO VERIFICADO. É opcional enquanto as cavernas estiverem adiadas. As faces de rocha
    fechadas precisam ser compradas ou modeladas.
  - **Cavernas, se o dono quiser (decisão 11):**
    [Modular Cave](https://marketplace.unity.com/packages/3d/environments/dungeons/modular-cave-ancient-cave-ancient-ruins-mayan-ruins-cavern-306634)
    a $79,99 ou [Modular Caves](https://marketplace.unity.com/packages/3d/environments/dungeons/modular-caves-112763) a $19,99 [V].
- **Custo [E].** De 7 a 9 ms por fora (rocha opaca, poucas plantas). Com o chão a 60 m, o terreno distante só aparece do cume e da
  torre de resfriamento, como fundo. As paredes de rocha são o segundo lugar para testar GPU occlusion culling.
- **Construção.** Sistema novo: faces de rocha fechadas. A vista distante (SIS-4) foi adiada. As cavernas (buracos no terreno) exigem
  ligar o "Terrain Hole" no asset do HDRP, ou elas somem no build (`terrain-lit-material.md`). **As cavernas ficam adiadas.** A toca
  do bando B é um tubo de lava com entrada de malha (sem buraco no terreno) e um único cômodo raso.

### 3.8 B6 Charneca de sotavento

- **Onde.** No lado seco a leste e sudeste, entre os cercados e a costa (Z5) e em volta do complexo de pesquisa (Z6).
- **À noite.** Arbustos escuros e densos de 1 a 3 m, matacões claros que pegam o luar e névoa fina. É a paleta mais quente da ilha
  (castanho e cinza-esverdeado). Cicas só nas encostas quentes junto ao Vale Quente.
- **Som.** Vento em folhas duras, insetos (a noite de insetos mais alta) e pedras sob os pés.
- **Furtividade.**
  - **Visão:** 20 a 35 % dos raios livres além de 30 m.
  - **Cobertura:** arbustos de 1,9 a 3,2 m escondem o jogador em pé, a melhor cobertura aberta fora das matas, mas só os matacões
    são cobertura dura. As copas são densas até o chão, então os volumes de ocultação (seção 6.1) pesam mais aqui.
  - **Chão:** cascalho arenoso 1,2, pedras 1,3, granito 1,0.
  - **Quem passa:** o T. rex anda entre as touceiras.
- **Fontes: uma coleção CC0 coerente (`namaqualand`), com LODs próprios** [V].
  - **Arbustos:** `searsia_burchellii` (1,9 a 3,2 m, LOD0 a LOD2), `searsia_lucida`, `othonna_cerarioides` (LOD0 a LOD3),
    `didelta_spinosa` e `wild_rooibos_bush`.
  - **Madeira morta:** `dead_quiver_trunk` e `dead_quiver_branch_01` e `_02`.
  - **Rochas:** `namaqualand_boulder_02` a `_06`, `_boulders_01`, `_rocks_01`, `_stones_01` e `_cliff_01` e `_02`.
  - **Chão:** `sandy_gravel_02` como camada própria; `gravelly_sand`, `tiger_rock` e `red_laterite_soil_stones`, mais `Ground096A`
    a `C` e `Rocks012` a `021` do ambientCG, como decals.
  - **Ficam de fora** as flores, as suculentas e as babosas ou *quiver trees*, exceto em solo quente, porque se leriam como deserto
    (seção 2.1). Nenhuma compra é necessária.
- **Custo [E].** De 8 a 10 ms. A `searsia_burchellii_large` tem 361.558 triângulos no LOD0, então use os LODs próprios e o afinador.
- **Construção.** **Quase de graça.** Usa o mesmo pipeline de manifesto à dispersão, com um kit novo.

### 3.9 B7 Costa e B8 Instalações

**B7 Costa.**

- **Onde.** Uma faixa de 50 m ao longo de todo o litoral, mais o terraço da pista a leste (capim costeiro), o promontório do farol a
  nordeste (só fundo), a Enseada do Naufrágio e a enseada do porto.
- **Relevo.** A costa oeste, a barlavento, é falésia alta, o que fecha a ilha sem jogabilidade de água. Nas praias, o jogador é
  barrado com água pelo joelho.
- **À noite.** Horizonte, caminho de lua no mar, a arrebentação como linhas pálidas, araucárias nos promontórios e arbustos costeiros
  dobrados pelo vento. É o bioma mais claro da ilha.
- **Som.** A arrebentação é uma zona de máscara: ruído x0,5 a até 30 m das ondas [E]. Também vento e a buzina do porto.
- **Furtividade.**
  - **Visão:** na praia, 80 a 95 % dos raios livres além de 30 m. A costa é para atravessar, não para ficar.
  - **Cobertura:** só nas cavas das dunas e sob as falésias.
  - **Chão:** areia seca 0,6, areia molhada 0,5, rocha 1,2, cascalho de praia alto.
- **Fontes.**
  - **CC0 (coleção `smugglers_cove`):**
    - falésias `coastal_cliff_01`, `_02` e `_04` (0,46 a 1,54 M triângulos, então precisam de decimação);
    - rochas `coast_rocks_*`, `coast_land_rocks_02` a `_04`, `coast_line_01` e `_02`;
    - arbustos costeiros `island_tree_01` a `_03`, `grass_bermuda_01` e `modular_wooden_pier`;
    - chão `coast_sand_02` e `damp_beach_sand` como camadas próprias; `coast_sand_01`, `_03` a `_05`, `low_tide_rocks` e
      `seaside_rock`, mais `Ground092A` a `095C` do ambientCG, como malhas e decals.
  - **Compra:** NatureManufacture
    [Coast & Dunes](https://marketplace.unity.com/packages/3d/vegetation/coast-dunes-environment-dynamic-nature-beach-sea-islands-dry-des-309456),
    $60, HDRP na 6000.3, versão 1.1.1 de maio de 2026; o conteúdo é NÃO VERIFICADO.
- **Custo [E].** De 7,5 a 9,5 ms, mais o mar. O HDRP Water System não foi medido no M4 Max; o número de planejamento é de 0,5 a 3 ms,
  NÃO VERIFICADO. A demo usa só o plano mínimo da enseada (seção 2.5); o porto (Z2) decide entre esse plano melhorado e o Water
  System, medindo os dois.
- **Construção.** Sistemas novos: a superfície do mar (só visual) com barreira na linha d'água e o capim de duna em escala (SIS-9).

**B8 Instalações.**

- **Onde.** Os 22 locais ativos (seção 5.1). O ponto e a área de cada um estão no JSON.
- **À noite.** Poças de luz de sódio ou LED das poucas lâmpadas que ainda funcionam (exposição!), grama de jardim crescida, concreto
  rachado por raízes, cicas ornamentais em canteiros e cercas caídas no mato.
- **Som.** Zumbido de gerador, lâmpadas chiando, uma lona batendo e uma porta batendo ao vento. **Uma cerca viva tiquetaqueia uma vez
  por segundo**, porque os energizadores de cerca para animais pulsam de 45 a 60 vezes por minuto
  ([Texas A&M](https://agrilifeextension.tamu.edu/library/wildlife-nature-environment/deterring-bears-with-electrified-fences-a-beginners-guide))
  [V]. Cerca em silêncio é cerca morta.
- **Furtividade.**
  - **Visão:** 20 a 60 % dos raios livres além de 30 m, conforme os prédios.
  - **Cobertura:** muita cobertura dura (paredes, contêineres, veículos, tubulações). É a cobertura de pé que a floresta não tem.
  - **Luz:** é o perigo daqui, e também o que permite observar antes de entrar (regra LV-3).
  - **Chão:** concreto 1,0, cascalho 1,3, grade metálica 1,8.
- **O chão de B8 sai do terreno.** Estradas, pátios e plataformas são malhas ou decals do gerador de instalações, e os gramados são
  cobertura do chão (SIS-9). Isso libera as camadas do terreno para os biomas (tabela 3.10).
- **Fontes.**
  - **Prédios gerados por código a partir de peças paramétricas** (`facility.md` 2.2):
    - contêiner de 20 pés (6,06 x 2,44 x 2,59 m) sobre estacas de 0,6 a 0,9 m;
    - cabana Quonset de 6,1 x 14,6 m;
    - bunker com paredes de 0,4 a 0,6 m;
    - galpão de pórtico com vão de 12 a 30 m;
    - torres treliçadas de 12 a 20 m e o mastro de 75 m;
    - cerca de malha de 3 a 5 m em postes a cada 3 m, com portões em **eclusa dupla**;
    - portões de concreto e aço de 5 m nos passos;
    - tubulações de 0,6 a 1,2 m.
  - **Texturas CC0** de concreto, metal e chapa enferrujada (os ids exatos são NÃO VERIFICADO), `Fence001` a `006` (malha), e
    `MetalWalkway`, `Asphalt` e `Concrete` do ambientCG.
  - **Props:** `street_lamp_01` e `_02`, `modular_electricity_poles`, `fire_hydrant`, `water_manhole_cover` e `trashbag`. Jardim:
    `grass_medium_01` e `_02` e ervas daninhas. Canteiros: `anthurium_botany_01`, `calathea_orbifolia_01`, `pachira_aquatica_01` e
    `potted_plant_*`.
  - **Interiores:** só 6 interiores completos no jogo (seção 5.1); os outros locais têm no máximo uma sala. Precisam de props
    comprados: **$150 a $400** em 3 a 5 pacotes [E], por exemplo o
    [Laboratory PACK HDRP](https://assetstore.unity.com/packages/3d/props/furniture/laboratory-pack-hdrp-181195) a $35 (v1.0, 2020)
    [V, `scope.md` 15]. A maioria dos pacotes de laboratório não é HDRP. O Laboratory Multipurpose Pack
    ([$9,99](https://marketplace.unity.com/packages/3d/environments/laboratory-multipurpose-pack-hdrp-186787)) provavelmente fica
    abaixo do realismo da floresta.
  - **Luz de interior (SIS-10):** a floresta não tem luz indireta assada; o HDRP ilumina tudo pela sonda de ambiente do céu
    (spec da floresta 8.5: `"apv": "none"`) [M]. Sem cuidado, um interior fica com a luz do céu sem oclusão, clareada pela
    autoexposição (até 10 EV), e cheio da névoa de altura global. Cada interior ganha um Volume local gerado com ele: multiplicadores
    de luz indireta perto de 0, um override de névoa e limites de exposição. A Quonset da demo (sonda SD12) decide se isso basta ou se
    é preciso assar Adaptive Probe Volumes por local (o HDRP 17.6 faz streaming de APV, `probevolumes-streaming.md`) [V].
- **Custo [E].** De 8 a 10 ms, com as paredes como bons oclusores. A lanterna sozinha custa 0,5 a 1,3 ms [M, N1], então **no máximo
  duas lâmpadas com sombra ficam à vista além dela**; as outras iluminam sem sombra.
- **Jurídico.** Malha genérica e postes de concreto. Nada de portão de madeira, sinalização de parque ou arquitetura de centro de
  visitantes.

### 3.10 Paletas de terreno (4 compartilhadas, mais 1 por bioma)

Estão em `terrainLayers` no JSON, que o gerador lê. Todas são CC0 do Poly Haven.

| Grupo | Camadas | Uso |
|---|---|---|
| **Compartilhadas (4)** | `forest_ground_06` (húmus), `mud_forest` (lama), `rocks_ground_05` (cascalho), `rocky_terrain_02` (rocha escura) | Em todo tile. São a base de toda borda entre biomas |
| B1 | `forest_leaves_04` | Agulhas e folhas |
| B2 | `rock_pitted_mossy` | Rocha molhada com musgo |
| B3 | `dry_decay_leaves` | Serapilheira de samambaia |
| B4 | `burned_ground_01` | Carvão; lascas e cinza como decals |
| B5 | `lichen_rock` | Basalto com líquen |
| B6 | `sandy_gravel_02` | Cascalho arenoso |
| B7 | `coast_sand_02`, `damp_beach_sand` | Areia seca e molhada |
| B8 | nenhuma | Malhas e decals do gerador de instalações |

**Resultado no mapa v2** (`docs/design/ilha-trabalho/tools/tile-layers-v2.py`, biomas com pelo menos 400 m² num tile):

- 40 dos 43 tiles com terra ficam em 8 camadas ou menos. Os cinco tiles da demo ficam com 7 ou 8: T(-4,-1) 7, T(-3,-2) 8, T(-3,-1)
  7, T(-2,-2) 8 e T(-2,-1) 7.
- Três tiles chegam a 9: T(-2,1), T(-1,0) e T(1,-2). Neles vale o **recuo**: o bioma de menor área no tile usa só as compartilhadas.
  São 768 a 2.688 m² (a costa em dois tiles, e a grota em T(-1,0)).
- A auditoria "camadas distintas por tile ≤ 8" roda no passo da ilha (seção 7.3), para o mapa não voltar a passar do limite.
- **Se o recuo ficar feio numa captura**, a saída é um Shader Graph de terreno no HDRP 17.6 (`terrain-master-stack-reference.md`):
  um mapa de índices mais um `Texture2DArray` de 16 a 32 camadas, com 2 a 4 camadas por texel. Isso acaba com o limite por tile, e
  o mesmo mapa de índices pode alimentar os multiplicadores de ruído do chão. Tamanho: M (3 a 6 agentes-dia) [E].

Antes de fixar a paleta, cada par de biomas vizinhos é conferido lado a lado com o preset noturno e a lanterna. A keyword de 8
camadas dos materiais feitos por script também precisa de teste na paleta nova (spec 6.3).

---

## 4. Espécies

### 4.1 Elenco e ordem de progressão

Sete espécies. As fontes são `species.md`; os preços foram lidos nas páginas da Asset Store em 2026-10-21 [V]. Todo valor de jogo é
[D].

**Só quatro espécies têm IA completa.** O compy, o Dilophosaurus, os raptors e o T. rex caçam, procuram e reagem. O Triceratops só
tem a carga; o Parasaurolophus e o Brachiosaurus andam por splines. Isso corta animações pareadas (combate entre animais), um
agente GG para as manadas e cerca de 4 a 8 agentes-dia [E, `scope.md` 11].

| Ordem | Espécie | Nível | IA | Papel no diretor (6.8) | Grupo | Território (id no JSON) | Primeira aparição | Compra |
|---|---|---|---|---|---|---|---|---|
| 1 | Parasaurolophus | 0 (informação) | spline e chamados | manada | 4 a 8 | PARA-1 (P-02, 2,5 ha); PARA-2 (Z1, 3,6 ha) | Demo: só ouvido (os dois chamados) | F4, $55 |
| 2 | Compsognathus | 1 (primeira ameaça) | completa | pressão | 4 a 10 | COMPY-1 a 4 (2,5 a 8,2 ha) | Demo, de 3 a 8 min, sozinho (cena CE1) | F0, $9,99 |
| 3 | Dilophosaurus | 2 | completa | perigo fixo | 1 | DILO-03 (demo, 1,6 ha), DILO-01 (2,0 ha), DILO-02 (1,8 ha) | Demo: se mostra no galpão (CE2); clímax na volta (CE3) | F0, $50 |
| 4 | Deinonychus ("raptors") | 3 | completa (`RaptorAI`) | caçador | 3 (no máximo 4) | DEIN-A (Z6 e Estrada Leste, 19,6 ha), DEIN-A-VILA (6,7 ha), DEIN-B (Z4 e cume, 13,9 ha), DEIN-A3 e DEIN-B3 | Gancho no fim da demo (CE6); vivo na vila no passo 2a (CE8) | F0, $35 |
| 5 | Tyrannosaurus rex | 4 (ápice) | completa | caçador | 1 | T-REX-1 (bacia, 21,1 ha); T-REX-2 (planalto, O7 ou passo 3); T-REX-3 (ato 4) | Um rugido na torre da demo; de perto no portão do Passo Sul, no hub (CE7) | F4, $60 |
| 6 | Triceratops | 2 (perigo) | só a carga | perigo fixo | 1 a 3 | TRI-1 (P-03), TRI-2 (mãe e filhote), raio de 35 a 40 m | TRI-2, na Estrada Leste (CE9) | F4, $24 |
| 7 | Brachiosaurus | ambiente | spline | manada | 2 | BRACH (P-04, 6,6 ha) | Pela T5 ou da Torre B (CE11) | F5, $60 |

**O que muda em relação à visão preliminar do coordenador** (com evidência em `species.md`):

1. **Os raptors são Deinonychus com penas.** O raptor do filme tem o tamanho do *Deinonychus*. O Velociraptor real tinha de 1,5 a
   2,07 m e de 14 a 20 kg, e não sustenta o papel de antagonista em primeira pessoa
   ([Velociraptor](https://en.wikipedia.org/wiki/Velociraptor), [Deinonychus](https://en.wikipedia.org/wiki/Deinonychus)) [V].
   Penas são ao mesmo tempo ciência e distância jurídica. O nome final é a decisão 5.
2. **O Gallimimus fica adiado.** O papel de estouro dele repete o do Parasaurolophus, e os modelos ou são pesados (61.814 triângulos,
   sem LOD) ou toscos (1.626 triângulos).
3. **O T. rex caça pelo cheiro e por som grave à noite**, não por movimento. O estudo de Stevens (2006) achou 55 graus de visão
   binocular e acuidade de até 13 vezes a humana. A óptica do olho sugere visão fraca com pouca luz, mas essa vem de uma fonte só
   ([Science News](https://www.sciencenews.org/article/sight-saur-eyes-t-rex-vision-was-among-natures-best)) [V].
4. **O Dilophosaurus é nível 2 por ser previsível, não por ser fraco.** Ele tem 6 a 7 m e 283 a 400 kg, cerca de quatro vezes a massa
   de um Deinonychus ([Wikipedia](https://en.wikipedia.org/wiki/Dilophosaurus)) [V]. Por isso o diretor nunca o manda atrás do
   jogador: ele fica nos pontos de emboscada dele.
5. **A ordem do compy primeiro está certa** (`horror.md`, resposta 3): compys, Dilophosaurus, raptors, T. rex, com os herbívoros
   como informação e perigo lateral.

**O que o jogador aprende, em ordem.** Ouvir (os chamados da manada); o ruído tem consequências (compys); ler o terreno e responder
a um aviso (Dilophosaurus); disciplina de luz e esconderijo, e portas estreitas (raptors na vila); cheiro, vento e quebra de rastro
(T. rex no portão do hub, sem risco); territórios e usar um animal contra outro (Triceratops); cobertura de ruído (saurópode). O
domínio do T. rex vem depois, no planalto e no ato 4.

### 4.2 Sentidos e valores iniciais da IA

A referência é o `RaptorAI` atual: visão de 28 m em 110 graus (um jogador agachado só é visto até 0,6 x 28 = 16,8 m), detecção de
0,25 s perto a 2,2 s a 28 m, e perseguição a 9 m/s. Os valores abaixo são [D] de `species.md` seção 3, revistos pelas revisões. A
distância a que cada espécie vê o facho, em cada estado da lanterna, está na tabela 6.6; os avisos que o jogador ouve estão na
tabela 6.5; o tempo até a captura está na tabela 6.4.

| Espécie | Visão dia / noite (lanterna apagada) | Campo de visão | Audição | Olfato | Reação à luz | Velocidade |
|---|---|---|---|---|---|---|
| Parasaurolophus | 60 / 25 m | 300 graus | x1,5 (grave) | 30 m | Facho na cara a menos de 15 m: alarme de "desconhecido" e fuga | 8 m/s, só nas faixas de estouro |
| Compsognathus | 15 / 8 m | 270 graus | x1,2 | 30 m (carniça, sangue) | Atraído além de 10 m; facho em Alta a menos de 5 m o espalha e faz guinchar | 7 m/s em arrancadas de no máximo 3 s |
| Dilophosaurus | 25 / 12 m | 140 graus | x1,0 | 20 m | Além de 12 m congela e se reposiciona; a menos de 12 m faz a exibição (aviso de 1,5 s) | Bote de 10 m/s até 12 m; perseguição de 6,5 m/s por 10 s |
| Triceratops | 20 / 10 m | 200 graus | x0,8 | 30 m | Facho na cara a menos de 15 m, ou o jogador a menos de 15 m do filhote: aviso de 1,5 s e carga | 7,5 m/s por até 40 m |
| Deinonychus | 40 / 20 m | 120 graus | x1,5 | 40 m a favor do vento | Investiga a mancha de luz: luz significa presa | 9 m/s; salto de 4 m |
| Brachiosaurus | ignora | | | | nenhuma | 2 m/s |
| T. rex | 100 m ou mais / 15 m (30 m para a lâmpada em Alta) | 150 graus (55 binoculares) | x1,0; tipo grave a 200 m | 150 m a favor do vento, x2 com sangue | Pouca à noite; vista plena no amanhecer | 8 m/s depois de 2 s; curvas de no máximo 45 graus por segundo |

**Duas regras de luz que o jogador consegue aprender** (em vez de seis limiares invisíveis):

- **Caçadores vêm até a luz:** raptors, e compys além de 10 m. O T. rex quase não liga para a luz à noite.
- **O resto odeia luz na cara a menos de uns 15 m:** Dilophosaurus, Triceratops e Parasaurolophus.
- **Brilho dos olhos visível quer dizer "você está no alcance dele".**

**O que a IA atual precisa ganhar** (decisão 8):

- cheiro com vento, que muda por passo (T. rex, raptors, compys), e o jogador escondido continua cheirando;
- um **tipo de ruído grave** (gerador, motor, queda pesada) ouvido pelo T. rex a 200 m;
- uma checagem de **visibilidade do facho** por estado da lanterna, separada da visão do corpo (todos os predadores);
- a **checagem de esconderijo** com motivo, chance por passo e aviso (seção 6.1);
- **avisos audíveis por estado** e a regra de justiça do flanco (seção 6.5);
- alarmes como eventos de ruído e de informação (Parasaurolophus);
- papéis de bando (raptors e compys) e papéis no diretor (seção 6.8);
- volumes de ocultação (seção 6.1);
- a **ameaça do Triceratops** como evento: um raptor que persegue desiste quando um Triceratops carrega a menos de 15 m dele.

O vento já existe e já move partículas e folhagem, então o jogador consegue ler de onde ele sopra.

### 4.3 Parasaurolophus

- **O animal real.** Indivíduo-tipo de uns 9,45 m; um adulto médio tem 7,5 m e 2,6 t (Paul). O crânio funcionava como ressonador, com
  chamados estimados de 55 a 720 Hz (Weishampel 1981)
  ([Wikipedia](https://en.wikipedia.org/wiki/Parasaurolophus)) [V]. A vida em manada é inferida dos hadrossauros em geral.
- **Papel.** O alarme da ilha. Um membro da manada que vê um predador, ou o jogador, a menos de 25 m, ou que ouve um ruído alto, dá
  um chamado retumbante que vai a 300 m [D]. **A informação corre nos dois sentidos**: o chamado diz ao jogador onde está o perigo, e
  o susto da manada diz aos predadores onde está o jogador (um evento de ruído de 30 m de raio na manada).
- **Os dois chamados diferem pelo ritmo, não só pela altura.** "Desconhecido" são três chamados curtos; "predador" é um chamado
  longo. A demo toca os dois uma vez cada, sem modelo: "desconhecido" quando o jogador cruza o paliteiro, e "predador" logo antes do
  gancho dos raptors (cena CE6).
- **Comportamento.** Ativo dia e noite. Pasta nos prados úmidos e nas cavalinhas. A manada anda por splines e, assustada, dispara a
  8 m/s por **faixas de fuga desenhadas**, para longe do que a assustou, depois de 2 a 3 s de ronco no chão e do alarme. Um jogador
  agachado atrás de cobertura de 1 m ou mais é contornado (raio de desvio de 2 m). Um raspão derruba e faz sangrar; não mata.
- **Território.** PARA-1 no cercado P-02 (Z5) e PARA-2 na clareira queimada de Z1 (só ouvida na demo; vista por spline nos passos 2a
  e 2b).
- **Como sobreviver e como usar.** Não entrar no campo de visão da manada e não ficar numa faixa de fuga. Assustá-la de propósito para
  cobrir uma corrida, pagando o preço de atrair raptors.
- **Evitar.** Risco jurídico baixo.
- **Modelo.** Pxltiger,
  [Parasaurolophus (walkeri, cyrtocristatus)](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/parasaurolophus-walkeri-cyrtocristatus-dinosaur-223710),
  **$55** [V], comprado na F4.
  - LOD0 10.406, LOD1 4.462, LOD2 1.936 triângulos.
  - Texturas 4096.
  - 158 arquivos de animação, com conjuntos de quatro e de duas patas, trote, galope, alarme e comer, bebendo, deitar, dormir e
    ataques.
  - Inclui duas espécies (crista longa e curta).
  - Built-in.

### 4.4 Compsognathus

- **O animal real.** O espécime francês, o maior, tem de 1,25 a 1,4 m de comprimento (com a cauda), 29 cm no quadril e de 2,5 a
  3,5 kg. Comia lagartos ([Wikipedia](https://en.wikipedia.org/wiki/Compsognathus)) [V]. Não há evidência de bandos nem de ataque
  a animais grandes; os bandos são ficção do jogo.
- **Papel.** A primeira ameaça e a pressão ambiente do mundo aberto. **Compys nunca matam**: eles ferem, seguram e chamam quem mata.
  - **Sozinho é inofensivo.** Uma mordiscada é um susto e um guincho; ele foge de um chute.
  - **Em grupo de cinco ou mais**, acossa um jogador Sangrando, Caído ou parado escondido. As mordiscadas levam a Sangrando, e um
    jogador Caído no meio do bando tem 5 s para se soltar chutando (seção 6.9).
  - **O perigo real é indireto.** O guincho é um evento de ruído de 25 m [D] que atrai os predadores da área. É o tutorial de "ruído
    tem consequência".
- **Comportamento.**
  - Segue o jogador a 6 a 15 m, **por no máximo 60 a 90 s**, a menos que ele esteja Sangrando ou carregue comida ou o galão. Perde o
    interesse se o jogador ficar parado fora da luz por 10 s.
  - **Depois de comer numa carcaça, o bando fica saciado por 10 minutos.** Só um bando segue o jogador de cada vez.
  - Arranca a 7 m/s por no máximo 3 s, mais rápido que o sprint de 6 m/s, depois para e se reagrupa.
  - Pode **expulsar** o jogador de um esconderijo, porque entra onde os grandes não entram (classe P, seção 6.1): arrasta-o para fora,
    Sangrando.
  - Foge do gerador ligado (é por isso que o bando 4 sai da pista no ato 4).
- **Território.** COMPY-1 (bordas da mata e clareira queimada em Z1), COMPY-2 (lixo da vila e câmara fria do porto), COMPY-3 (poço de
  incineração junto aos labs) e COMPY-4 (pista e parque de combustível, só até o passo 3).
- **Como sobreviver.** Chutar (alcance de 1 m, espalha 1 ou 2 por 3 s, ruído de 6 m), jogar comida (o bando come por 15 a 20 s) ou um
  objeto para puxar o bando, não parar, subir em rochas ou veículos e **nunca deixá-los guinchar em território de raptor**.
- **Lanterna.** Além de 10 m o facho os atrai por curiosidade. O facho em Alta em cheio a menos de 5 m os espalha por 3 a 5 s, mas o
  susto faz guinchar. A luz nunca sai de graça.
- **Evitar.** A mordida venenosa ou sedativa (do romance) e as cenas do filme: compys atacando uma menina na praia e um bando matando
  um caçador perdido.
- **Modelo.** DANKIE,
  [Prehistoric Dinosaur - Compsognathus](https://assetstore.unity.com/packages/3d/characters/animals/prehistoric-dinosaur-compsognathus-135639),
  **$9,99** [V], comprado na F0 com os outros dois corpos da demo.
  - 2.980 triângulos, bom para um bando de 10.
  - "Mais de 30 animações", com a lista fora da página (NÃO VERIFICADO).
  - 4 peles.
  - Versão 1.0 de janeiro de 2019, "sem avaliações suficientes" e sem pipeline declarado: **é a compra mais fraca da demo** [V,
    `scope.md` 17].
  - Reserva: hiepvu no Sketchfab, $27, 12.788 triângulos e 10 animações (NÃO VERIFICADO). Se as animações da DANKIE falharem na
    sonda de importação (SD17), a troca é imediata.

### 4.5 Dilophosaurus

- **O animal real.** O maior espécime tem uns 7 m e 400 kg; o holótipo, 6,03 m e 283 kg. Tinha duas cristas finas, provavelmente de
  exibição. Marsh e Rowe (2020) o descrevem como macropredador de mandíbula robusta
  ([Wikipedia](https://en.wikipedia.org/wiki/Dilophosaurus),
  [National Geographic](https://www.nationalgeographic.com/science/article/jurassic-park-got-almost-everything-wrong-about-iconic-dinosaur-dilophosaurus))
  [V]. O habitat real era "um sistema de rios num ambiente sazonalmente seco": é a grota de córrego sazonal.
- **Papel.** O primeiro grande predador e o clímax da demo. Um animal por território de 1,6 a 2 ha de samambaia densa e abetos jovens
  junto a um córrego ou trilha de caça. Espera nos pontos de emboscada e não sai do território. O diretor só sobe ou baixa o alerta
  dele; nunca o manda atrás do jogador (papel "perigo fixo", seção 6.8).
- **A luz e o aviso** (as respostas estão escritas, para que o aviso tenha uma saída):
  - **Além de 12 m**, o facho o faz **congelar e depois se reposicionar em silêncio** para o próximo ponto de emboscada à frente do
    caminho do jogador. Ele nunca recua: varrer com a lanterna não afasta o clímax.
  - **A menos de 12 m, iluminado**, ele ergue a cabeça e **exibe a crista com um silvo grave por 1,5 s**. A resposta: cortar o facho
    ou desviá-lo **e** dar 2 m ou mais para o lado, ou ficar atrás de cobertura dura, dentro desses 1,5 s. O bote vai para o último
    ponto iluminado e erra. Manter o facho nele faz o bote acertar.
  - **Sem luz**, um silvo seco audível a 10 m vem 1,0 s antes do bote. É o aviso no escuro.
  - Esse aviso se apoia na ciência da crista como exibição; o filme usa um colar.
- **O bote.** 10 m/s, alcance de até 12 m. **O primeiro bote do jogo derruba e faz sangrar, mas não mata**; depois disso, bote é
  morte. Um bote que erra deixa o animal 1,5 s se recompondo, e então ele persegue a 6,5 m/s por 10 s e volta aos pontos de
  emboscada. Um jogador livre escapa (tabela 6.4); com o galão nas mãos (5 m/s), só escapa se largar o galão.
- **Sentidos.** Visão de 12 m à noite. Nota o movimento nas samambaias (os eventos `Rustled` do `FoliagePusher`).
- **Território.**
  - **DILO-03**, a grota baixa de Z1 (demo), em volta do Galpão de Combustível;
  - **DILO-01**, a Grota Grande alta, junto à ruína das estufas;
  - **DILO-02**, a Grota Norte, junto à estação de bombeamento.

  Usa o agente G (raio de 1,0 m), assado com os caules finos como obstáculo. Os pontos de emboscada têm de cair em área alcançável
  por G, e a auditoria informa que fração do núcleo de B1 o G alcança (seção 7.5).
- **A coleira do DL-03 foi arrancada.** Ela está no galpão e bipa o ritmo de morte: o receptor leva o jogador à coleira, não ao
  animal. É a reviravolta da regra 6 da seção 1.6, e a emboscada continua funcionando. DL-01 e DL-02 têm coleira (tabela 6.5).
- **Como sobreviver.** Ler os sinais: camas de samambaia amassadas, restos de presa, um relance da crista. Contornar as moitas em vez
  de atravessá-las. Jogar um objeto numa moita para ele se revelar ou atacar o ruído. Responder à exibição.
- **Evitar.** O colar, o cuspe (o pacote Pxltiger traz um clipe "Attack-Spit": **não usar**), as cores do filme e o chamado
  chocalhado do filme.
- **Modelo.** Pxltiger,
  [Dilophosaurus wetherilli](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dilophosaurus-wetherilli-286641),
  **$50** [V], comprado na F0.
  - **Sem colar**, crista dupla e protopenas esparsas.
  - LOD0 20.306, LOD1 7.908, LOD2 2.420. A página encabeça esses números como "Ankylosaurus", um erro de cópia: conferir na
    importação.
  - 121 animações, incluindo um **conjunto Sneak** (idle, frente, ré, esquerda, direita, início), mordidas, rugidos, beber e comer.
    **Não há clipe de exibição da crista**: só rugidos e 9 poses aditivas de cabeça. A exibição sai de um rugido mais uma pose
    aditiva; a matriz de cobertura (seção 7.6) confere isso antes de qualquer outra compra.
  - Built-in 2022.3.
  - **Evitar** o Dilophosaurus da Trextor15, que tem o colar do filme.

### 4.6 Triceratops

- **O animal real.** De 8 a 9 m e de 6 a 10 t. Os chifres e o escudo eram sobretudo de exibição, mas há ferimentos de combate
  cicatrizados, inclusive mordidas de tiranossauro num chifre. **Há pouca evidência de manadas**
  ([Wikipedia](https://en.wikipedia.org/wiki/Triceratops)) [V].
- **Papel.** Um perigo territorial, não um caçador. Solitário ou mãe com filhote, num território de 35 a 40 m de raio. Só tem a carga.
- **Comportamento.**
  - O facho no rosto a menos de 15 m, ou o jogador a menos de 15 m do filhote, dispara o **aviso de 1,5 s** (cabeça baixa, pata
    raspando) e depois uma carga de 7,5 m/s por até 40 m. Ela termina no fim dos 40 m ou numa árvore grossa.
  - **A carga só mata num acerto direto**, e o primeiro acerto do jogo derruba em vez de matar. Um passo de 3 m para o lado no último
    segundo a evita.
  - O mugido é um evento de ruído e um **evento de ameaça**: um raptor que persegue desiste quando um Triceratops carrega a menos de
    15 m dele. Não há luta entre animais nem animações pareadas.
- **Uso sistêmico.** Atrair os raptors para o território dele, onde a carga os espanta. É o lugar natural para usar o truque contra o
  par do bando A, que patrulha a Estrada Leste desde o passo 2a.
- **Território.** TRI-1 na estação de alimentação do cercado P-03 e TRI-2 (mãe e filhote) na clareira da Estrada Leste, entre os
  laboratórios e o controle dos cercados. A R5 atravessa o TRI-2, e é ali a apresentação (cena CE9).
- **Como sobreviver.** Ficar atrás de troncos com mais de 40 cm ou de rochas, quebrar a linha de visão e sair do território.
- **Evitar.** A cena do Triceratops doente com o veterinário e a encenação do passeio do filme.
- **Modelo.** Pxltiger,
  [Realistic dinosaur Triceratops](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/realistic-dinosaur-triceratops-126881),
  **$24** [V], comprado na F4.
  - LOD0 9.224 a LOD3 496 triângulos.
  - 91 animações e 9 poses, com um **conjunto agressivo** completo (idle, andar, carga, ataque).
  - Built-in.

### 4.7 Deinonychus (os "raptors")

- **O animal real.** Até 3,3 a 3,4 m e de 60 a 100 kg. Há achados de vários indivíduos com presas e pistas paralelas; a caça em bando
  é uma hipótese razoável, mas contestada ([Wikipedia](https://en.wikipedia.org/wiki/Deinonychus)) [V]. Os anéis escleróticos de
  dromeossaurídeos sugerem hábito noturno, num método debatido
  ([NPR, Schmitz e Motani 2011](https://www.npr.org/2011/04/14/135385317/dinosaur-eyes-yield-clues-to-hunting-habits)).
- **Papel.** O antagonista principal. Há dois bandos de 3 (no máximo 4), ativos do anoitecer ao amanhecer:
  - **bando A**, com toca na Casa dos Carnívoros (Z6), a casa original deles, para onde voltam. Dois dos três têm coleira;
  - **bando B**, com toca num tubo de lava em Z4. Nenhum tem coleira.
- **Comportamento de bando, liberado por passo** (tabela 5.4):
  - **Passo 2a:** em par. Um checa o ruído enquanto o outro circula. A checagem de esconderijo começa aqui, com motivo (seção 6.1).
  - **Passo 2b:** em trio, sem flanco. **Adaptação com aviso**: na segunda distração no mesmo lugar, o raptor checa o ponto de queda
    por até 3 s e depois olha para trás ao longo da linha do arremesso; na terceira (a menos de 10 m do mesmo ponto e em 5 minutos),
    ele se vira e investiga de onde o objeto veio.
  - **Passo 3:** bando completo com **flanco com aviso**: um se mostra no facho e dois fecham a 90 graus dele, mas quem flanqueia
    vocaliza ao começar a fechar. A regra de justiça da seção 6.5 vale sempre: nenhuma morte vinda de fora do campo de visão sem um
    aviso espacializado nos últimos 2 s e pelo menos 1,0 s antes do contato. O "chamado de batedor" traz os outros em 8 s.
  - Isso os faz parecer inteligentes sem abrir portas como no filme. **Raptors não abrem portas neste jogo.**
- **Portas e aberturas.** Abertura de 1,5 m ou mais deixa passar raptor; de 0,9 m ou menos, barra; entre as duas é proibido nas
  instalações, para a regra ficar limpa (seção 7.5). **Porta estreita intacta é segura contra raptor, mas não contra compy**: é uma
  regra que o jogador aprende na vila (cena CE8). A linguagem visual: batente arranhado, porta arrancada e mancha no chão querem dizer
  "raptor passa"; porta estreita inteira quer dizer "não passa". Um raptor do lado de fora de uma porta estreita espera ali até o
  diretor o puxar para longe. A tabela por prédio está na seção 6.3.
- **Território por passo.**
  - **Ato 1:** só ouvidos, mais o gancho encenado no fim da demo.
  - **Passos 2a e 2b:** o par do bando A no complexo, na Estrada Leste até S19 e S20 (DEIN-A) e na vila (DEIN-A-VILA); o par do bando
    B na cumeada e na usina (DEIN-B).
  - **Passo 3:** bandos completos; o A se amplia para a vila inteira, o Portão da Mata e o porto (DEIN-A3), e o B para as grotas altas
    (DEIN-B3).
  - **Ato 4:** convergem para o cume e para a rota da pista.
- **Lanterna.** É a melhor pista que eles têm: luz significa presa. Eles veem o facho em Alta e a mancha que ele ilumina a 60 m, o
  brilho através da folhagem a 40 m, e o facho em Baixa a 20 m (tabela 6.6). O sinal para o jogador é o **brilho dos olhos** a 1 m de
  altura, com espaçamento de raptor ("dois pontos nas samambaias"). O tapetum em dinossauros é especulativo, então isso é licença
  artística.
- **Como sobreviver.** Lanterna em Baixa ou apagada, agachar em cobertura densa, usar esconderijos, prender a respiração quando um deles
  checa, jogar objetos para dividir o bando, levá-los ao território do Triceratops, entrar por uma porta estreita.
- **Evitar.**
  - um "Velociraptor" sem penas do tamanho do Deinonychus com a cabeça do filme;
  - a crista de espinhos de *JP III*;
  - as listras e o nome de "Blue";
  - raptors treinados, o cercado dos raptors e a abertura de portas;
  - o ataque pelo lado do "clever girl" (o flanco aqui sempre tem aviso);
  - a caçada na cozinha.
- **Modelo.** Pxltiger,
  [Deinonychus](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/deinonychus-182587), **$35** [V], comprado na
  F0: é o corpo do `RaptorAI` que já existe e o primeiro modelo da sonda de importação.
  - **Todo emplumado**; a construção das penas (cascas ou cartões com alpha) não está declarada e pesa no mapa de sombra da lanterna.
  - LOD0 12.644, LOD1 6.123, LOD2 2.988 triângulos.
  - 90 animações, com conjunto Sneak, ataques de garra e salto, rugidos, dormir e acordar.
  - Built-in; testado na 2021.3.16f1 [V, página lida em 2026-10-22].
  - **Retexturizar para tons escuros mosqueados**: o laranja listrado do pacote não serve para a noite.
  - Reserva escamosa: Rifat_Bilalov
    [Velociraptor](https://assetstore.unity.com/packages/3d/characters/animals/velociraptor-raptor-238785), $29,99, com 105 animações.

### 4.8 Brachiosaurus e T. rex

**Brachiosaurus**

- **O animal real.** De 18 a 22 m de comprimento, 9,4 a 13 m de altura e 28 a 47 t. Não se erguia nas patas traseiras
  ([Wikipedia](https://en.wikipedia.org/wiki/Brachiosaurus)) [V].
- **Papel.** Um marco vivo e uma ferramenta.
  - **É ouvido antes de ser visto:** pisadas que tremem a câmera e o controle a menos de 60 m.
  - **Cobertura de ruído:** a menos de 25 m de um saurópode andando, o raio de ruído do jogador cai pela metade.
  - **Perigo:** a menos de 8 m das patas ou atrás da cauda, o jogador é **derrubado e sangra**; não morre.
  - Ignora o jogador.
- **Território.** Dois animais no campo P-04 (BRACH), andando por uma spline (`sauropodRoute` no JSON). Não usa NavMesh. Ao longo da
  rota, o espaço entre os troncos é maior que 6 m.
- **Evitar.** A revelação com ele se erguendo, a trilha sonora do filme, a mastigação e o espirro.
- **Modelo.** Pxltiger,
  [Brachiosaurus (giraffatitan)](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/brachiosaurus-giraffatitan-160378),
  **$60** [V], comprado na F5. LOD0 11.954 triângulos e 64 animações. **Não encenar** o clipe "comer nas patas traseiras" como a
  revelação do filme. Há duas alternativas (decisão 11):
  - JUNNICHI SUKO
    [B. altithorax](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/brachiosaurus-altithorax-311025), $24,99,
    24.118 triângulos, a espécie verdadeira;
  - Pxltiger
    [Diplodocus](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/diplodocus-dinosaur-249856), $55, com uma
    silhueta diferente da do filme.

**Tyrannosaurus rex**

- **O animal real.**
  - **Tamanho:** Sue tem de 12,3 a 12,8 m e de 8,4 a 9,2 t.
  - **Velocidade:** a máxima fica em geral perto de 9 m/s, mas estudos de osso e músculo dão de 5 a 7,5 m/s.
  - **Sentidos:** o melhor olfato entre 21 dinossauros amostrados, e uma cóclea longa, melhor para sons graves (Witmer e Ridgely
    2009).
  - **Boca:** provavelmente tinha lábios cobrindo os dentes (Cullen et al. 2023)
    ([Wikipedia](https://en.wikipedia.org/wiki/Tyrannosaurus),
    [ScienceDaily](https://www.sciencedaily.com/releases/2023/03/230330172120.htm)) [V].
- **Papel.** O ápice do núcleo. Um animal só.
- **À noite ele caça pelo cheiro e pelo som grave.**
  - **Cheiro:** um cone a favor do vento de 150 m, que dobra com sangue fresco (jogador Sangrando). O vento muda no passo 3 e no ato 4
    (seção 2.1).
  - **O aviso do cheiro:** quando pega o rastro do jogador, ele dá **um bufo longo e grave audível a 150 a 200 m**. Isso fecha a
    faixa de 50 m em que, na v1, ele tinha o cheiro do jogador e o jogador não tinha nenhuma pista.
  - **Quebra de rastro:** lama, fumarolas, tambores de combustível e carcaças, **a cada 80 m ou menos** ao longo das rotas dentro dos
    territórios dele (`rules.scentBreakers` no JSON). Os córregos contam onde houver, mas os territórios dele (savana e planalto) têm
    poucos.
  - **Som grave:** geradores, motores e quedas pesadas são ouvidos a 200 m.
  - **Visão:** à noite ele vê o jogador a menos de 15 m e a lâmpada em Alta a menos de 30 m. Ao amanhecer, a 100 m ou mais, e **ficar
    parado não esconde ninguém**.
- **Movimento.** Persegue a **8 m/s depois de 2 s** de aceleração no aberto, e faz curvas de no máximo 45 graus por segundo. Isso dá
  de 12 a 19 s de perseguição, e o jogador escapa lendo o terreno: um refúgio que barra GG (ilha de abetos, vão de menos de 4 m,
  prédio) fica a até 60 m de todo ponto de rota no território dele (tabela 6.4). O agente GG tem direção própria, com raio mínimo de
  curva tirado dos clipes de virada em root motion, e anda por faixas com raio de curva de 15 m ou mais. Na mata (B1), só por faixas
  de 6 m ou mais, porque um cilindro de NavMesh não representa um animal de 12 m: cabeça e cauda atravessariam os troncos.
- **O primeiro contato do jogo não mata.** Na primeira vez que o T. rex alcança o jogador, ele morde a mochila: o jogador é jogado uns
  4 m, cai Sangrando, e o T. rex fica 5 s sacudindo a mochila. Depois disso, contato é morte.
- **Aviso.** Pisadas sentidas a 100 m, pelo tremor da câmera e pela vibração do controle; o bufo do cheiro a 150 a 200 m; respiração
  a 30 m.
- **O rugido é racionado:** no máximo um a cada 20 minutos, e cada um ligado a uma consequência visível (um alarme de manada, aves
  levantando da copa como um jorro de partículas, uma cerca que fica em silêncio).
- **Distração.** Só estímulos grandes funcionam: um alarme de carro, um sinalizador, uma carcaça, a explosão do parque de combustível.
- **Come uma carcaça estática** quando o diretor o deixa em Alívio. É a janela de "seguro enquanto ele come".
- **Território.**
  - **T-REX-1:** a bacia da caldeira (21,1 ha), cercada pelo anel com três passos, com névoa acumulada. É o "Cercado P-01" original.
    Os portões de concreto e aço dos passos o barram sempre; o jogador só entra pela porta de pessoal do Passo Sul, no hub.
  - **T-REX-2:** o planalto leste, **a partir do O7 ou do passo 3, o que vier primeiro**. O ronco da turbina religada (o som grave de
    que ele é especialista) o faz empurrar os destroços do Passo Leste arrombado. Antes disso, na linha C, ele é ouvido e cheirado
    no Passo Leste: vem até os destroços, bufa, faz o ferro ranger, e não sai (cena CE10).
  - **T-REX-3:** no ato 4, o caminho do cume à pista.
- **Evitar.**
  - a regra de visão por movimento;
  - o copo d'água tremendo;
  - a cabra presa;
  - a fuga pela cerca do cercado na chuva e a perseguição ao jipe com o espelho;
  - o nome "Rexy" e as cicatrizes dela;
  - a escultura da cabeça e o rugido do filme.
- **Modelo.** Pxltiger,
  [Dinosaur T-Rex](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-t-rex-175294), **$60** [V], versão 1.3
  de 2023-04-19, só Built-in, comprado na F4.
  - Visual moderno, com lábios.
  - LOD0 19.278 a LOD3 1.468 triângulos.
  - 104 animações, com um **conjunto Creep** de espreita, pausas de exploração, rugidos e comer. A matriz de cobertura confere se há
    clipe de farejar.
  - Um avaliador nota problemas no movimento dos pés (trecho de busca, NÃO VERIFICADO); a tabela de velocidade de raiz da sonda SD17
    confere.

### 4.9 Jurídico: as invenções da Universal que ficam de fora

Isto não é aconselhamento jurídico. Ideias como um dinossauro, um estouro de manada ou um predador noturno não são protegidas.
Expressões específicas (designs, esculturas, sons, cenas, personagens) e marcas são. Consultar um advogado de PI antes do lançamento.

| Área | Evitar |
|---|---|
| Nomes e marcas | Jurassic Park ou World, Isla Nublar, Isla Sorna, "Site B", InGen, Biosyn, Masrani, Hammond, "Mr. DNA", o logotipo do esqueleto, "Rexy", "Blue", Indominus e os híbridos, "lysine contingency", "spared no expense", "clever girl", "life finds a way" |
| Designs | O Dilophosaurus com colar ou cuspe; o "Velociraptor" sem penas com a cabeça do filme; os raptors de crista de *JP III*; a cabeça e as cores do T. rex do filme; a mordida venenosa do compy; as vocalizações dos filmes |
| Cenas e recursos | DNA de sapo; raptors abrindo portas; "não vê se você não se mexer"; o copo tremendo; a cabra; a fuga do cercado e o jipe; o Brachiosaurus se erguendo e espirrando; o Triceratops doente; raptors treinados; veículos de passeio, portão com tochas, centro de visitantes; o mosquito no âmbar; caçada de raptors na cozinha; religar a cerca com alguém escalando; o rádio da vila dos trabalhadores e o transmissor do cume **como final** |
| Lojas | Modelos que copiam o filme estão à venda abertamente (o Dilophosaurus com colar da Trextor15, o "Blue Raptor" do Sketchfab), e comprar um não licencia o design da Universal. Evitar pacotes "JP" da Muelsa, os "Hybrid" da Trextor15 e **ripagens de jogos**, que o Sketchfab hospeda com licenças CC falsas |
| Áudio | As vozes dos filmes ficam de fora. Bibliotecas compradas são matéria-prima: cada espécie tem um conjunto próprio, em camadas, e nenhuma espécie usa um chamado de estoque sem mudança (seção 8.4) |

A Universal faz valer esses direitos: mandou o fã-jogo *Jurassic Park: Operations* tirar toda a marca, e ele virou *Cretaceous
Kingdom* ([Inside the Magic, 2024](https://insidethemagic.net/2024/05/jurassic-park-fan-made-game-cretaceous-kingdom-dr1/)) [V].

**Adiadas ou opcionais.** O Gallimimus fica adiado (decisão 11). O Stegosaurus é uma 8ª espécie opcional (Pxltiger,
[$60](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-stegosaurus-189558), 78 animações), no papel de
perigo territorial com a cauda. Ficam excluídos Spinosaurus, Baryonyx, Mosasaurus e Pteranodon, pela regra de não ter água e pela
complexidade. As ovelhas da fazenda saíram com a fazenda, e as aves são um jorro de partículas.

---

## 5. Locais e objetivos

### 5.1 Os locais

São 22 locais ativos (3 deles opcionais), em quatro anéis de ameaça crescente: costa, baixada, cercados e núcleo. Ponto, área,
orientação, `status`, `interior` e `raptorAccess` estão em `sites` no JSON. As áreas vêm de `facility.md` 2.3, ajustadas ao tamanho
desta ilha.

**Interiores: no máximo 6 completos.** Nenhum interior foi construído neste projeto ainda, e cada cômodo precisa cumprir LV-5 a LV-7.
Os 6 interiores "herói" são S02, S08, S12 (salão das turbinas e sala de controle), S13, S15 e o escritório do S23. Os outros locais
são exterior mais **no máximo uma sala**. A vila tem 6 casas, das quais 2 entram, mais a enfermaria e o posto de segurança. A
Quonset da demo é a sonda SD12: o tempo, o custo de quadro e o visual dela recalculam os outros cinco antes da F5.

| Id | Local | Zona | Papel | Ameaça | Área | Interior |
|---|---|---|---|---|---|---|
| S01 | Enseada do Naufrágio | Z1 | Início, visto do alto; barco em chamas (L7) | compys na linha da maré | — | nenhum |
| S02 | Estação de Campo | Z1 | **Abrigo 1**; O1 e O3. Tem 1 cabana Quonset, 2 contêineres, gerador, mastro de rádio de 18 m, torre de 15 m (V1) e cerca de uns 140 m | compys; o gancho dos raptors | 40 x 30 m | herói (sonda SD12) |
| S03 | Galpão de Combustível | Z1 | O2, na boca da grota; a coleira arrancada do DL-03 | **Dilophosaurus DL-03** | 20 x 15 m | exterior |
| S04 | Portão da Mata | Z1 | Saída da demo (cadeado) | — | portão | exterior |
| S05 | Vila dos Funcionários | Z2 | 6 casas pré-fabricadas (2 entram: a casa A, de porta estreita, e a casa B, arrombada), **enfermaria (abrigo 2)** e posto de segurança (O5). Uma lâmpada acesa (L10) | compys; o par do bando A desde o passo 2a | 130 x 110 m | 4 salas |
| S06 | Depósito de Manutenção | Z2 | O4 (alicate) | compys | 50 x 40 m | 1 sala |
| S07 | Porto | Z2 | **Opcional**: píer, guindaste, escritório; o manifesto da evacuação (quem saiu, quem ficou) | compys na câmara fria | 110 x 70 m | 1 sala |
| S08 | Estação Militar | Z2 | **Abrigo 3: bunker de comando, o hub, na boca do Passo Sul** (125, -200). Sala de mapas; o teto (V7) olha o portão do passo; cena CE7 | baixa dentro; o T. rex do outro lado do portão | 100 x 80 m | herói |
| S11 | Estação de Bombeamento | Z3 | O6: água de resfriamento da usina | DL-02 | 30 x 25 m | 1 sala |
| S12 | Usina Geotérmica | Z4 | O7. Poços, separadores, tubulações, torre de resfriamento, salão das turbinas; **abrigo 4** (sala de controle) desde o passo 2a | bando B; zona barulhenta | 160 x 120 m | herói |
| S13 | Estação do Cume | Z7 | O12. Radome, mastro de 75 m (L1), gerador de reserva; **abrigo 7** | aproximação exposta; bando B; T. rex abaixo | 80 x 60 m | herói |
| S14 | Estação de Alimentação P-01 (ruína) | Z7 | **Opcional**: a história do T. rex; a coleira dele bipa "morte" aqui | T. rex | 40 x 30 m | 1 sala |
| S15 | Laboratórios e Incubação | Z6 | O9 (sala dos servidores). Incubadoras, berçário, eclusas, chuveiros de descontaminação, giroflex e sirene (L4); **abrigo 5** (escritório de quarentena) | bando A pelo corredor em anel | 120 x 90 m (bloco de 60 x 40 m com 2 andares, e anexos) | herói |
| S16 | Escritório de Segurança | Z6 | O8 (cartão 2); fotos de câmera impressas | bando A | 25 x 20 m | 1 sala |
| S17 | Clínica Veterinária | Z6 | **Opcional**: notas de comportamento, o "manual" de cada espécie; caixas de transporte como esconderijo | misto | 40 x 25 m | 1 sala |
| S18 | Casa dos Carnívoros | Z6 | Toca do bando A; corredor de tratador atrás da malha (você os vê, eles veem você) | bando A | 50 x 30 m | 1 sala (o corredor) |
| S19 | Controle dos Cercados | Z5 | O10; quadros de alimentação; frequências das coleiras; **a antena Yagi** | TRI-2 ao lado; o par do bando A; manada PARA-1 | 40 x 30 m | 1 sala |
| S20, S21 | Torres de Observação A e B | Z5 | Mirantes de 18 m (V3, V4); refúgio contra os grandes, com uma escada e uma saída só | um raptor esperando embaixo | 6 x 6 m | exterior |
| S22 | Abrigo do Saurópode | Z5 | O11; **abrigo 8** (concreto) | saurópodes; o T. rex depois do O7 ou no passo 3 | 15 x 10 m | 1 sala |
| S23 | Pista e Hangar | Z5 | **Abrigo 6** (escritório do hangar); O13. Pista militar antiga de 450 x 30 m, com bueiros de 0,9 a 1,2 m embaixo | campo aberto; o T. rex no ato 4 | 60 x 40 m mais a pista | herói (o escritório) |
| S24 | Parque de Combustível | Z5 | O gerador da pista (O13); uma explosão de uso único, a maior distração do jogo, que depois arde como marco | compys (bando 4) até o passo 3; raptors atraídos | 60 x 40 m | exterior |

**Cortados ou rebaixados na v2** (o JSON mantém os registros com `status`):

| Id | Antes | Agora | Por quê |
|---|---|---|---|
| S09 | Incinerador e Fossa (opcional) | um poço de incineração com carcaças, como prop, dentro do COMPY-3 | 1 a 3 agentes-dia por 2 a 5 min de horror ambiental |
| S10 | Estufas Geotérmicas (opcional) | ruína de vidro vista da R6, fora do corredor de Z3 | idem; a causa da flora vai para um documento |
| S25 | Farol (opcional) | luz de fundo L2; a R8b fica barrada | a R8 tinha 19 % das estradas só para um opcional |
| S26 | Fazenda (opcional) | cortada; a isca vira uma carcaça na pista | as ovelhas seriam um 8º animal com IA |
| S27 a S29 | Postos (opcionais) | cortados; os quadros de mapa vão para os abrigos | três locais para baterias e um esconderijo |

**A pista.** Com o resgate de helicóptero, a pista não precisa do comprimento de um avião STOL (`facility.md` pedia 600 a 900 m,
NÃO VERIFICADO). Os 450 m são a pista militar antiga, que cabe no terraço leste; as luzes dela marcam o pouso.

### 5.2 Tipos de trava

| Trava | Abre | Exemplo |
|---|---|---|
| Ferramenta | Uma classe de obstáculo em toda a ilha | O alicate corta portões de malha (O4) |
| Cartão (2 níveis) | Portas por nível | Nível 1 para portas de funcionários (O5); nível 2 para o interior dos labs e o abrigo do cume (O8) |
| Energia | Luzes, cercas vivas, terminais | A usina (O7) energiza Z4 a Z7, o mastro e os postes da R6 e da R4 |
| Conhecimento | Um código ou um caminho | A gravação da chefe dos tratadores dá o código do cume (O11) |
| Mão única | Um portão que só abre de um lado | As portas de pessoal dos passos Oeste e Leste (só de dentro da bacia); o portão alto da usina depois do O7; o portão leste dos labs depois do O9 |
| Território (suave) | Chão aberto que mata na hora errada | O planalto depois do O7 ou do passo 3 |

### 5.3 A cadeia de objetivos

Os tempos são de primeira jogada, [E] (`open-world.md` 4.2, recalculados para este mapa). As posições, os passos e os subpassos
(cada um com salvamento automático) estão em `objectives` no JSON.

| # | Objetivo | Zona e local | Precisa de | Abre e muda no mundo | Ameaça | Min |
|---|---|---|---|---|---|---|
| O1 | Chegar à Estação de Campo | Z1, S02 | — | Quadro de mapa e **mapa de papel**. Um bilhete diz que o gerador está sem diesel e que o galão ficou no galpão da grota | um compy sozinho | 8 a 10 |
| O2 | Buscar combustível no galpão da grota | Z1, S03 | O1 | A coleira arrancada do DL-03 bipa no galpão. Carregar o galão faz barulho de líquido (6 m andando, 12 m correndo) | **DL-03**; bando 1 na volta | 10 a 12 |
| O3 | Ligar o gerador, usar o rádio, subir na torre | Z1, S02 | O2 | **Abrigo 1** (salvar, bandagem). O rádio diz que o mastro do cume alcança o continente, mas precisa de energia, de um módulo e de um código, e marca as três linhas por alto no mapa. Chave do Portão da Mata. Vista da torre. **Fim do ato 1** | o gerador atrai o bando 1 (ruído de 40 m [D]) | 5 a 8 |
| O4 | Alicate de corte | Z2, S06 | O3 | Portões de malha da ilha toda: cercados, Cerca da Grota, complexo da bomba, cerca do complexo, S19 | compys; o par do bando A | 15 a 20 |
| O5 | Cartão nível 1 | Z2, S05 | O3 | Portas de funcionários: casa de bombas, salão das turbinas, portas externas dos labs | idem | 15 a 20 |
| O6 | Religar a estação de bombeamento (linha A) | Z3, S11 | O4 **ou** O5 | Água de resfriamento da usina; a luz da bomba acende | DL-02 e DL-01 nas grotas | 20 a 25 |
| O7 | Religar a usina: três disjuntores em três prédios (linha A) | Z4, S12 | O6 e O5 | Energia para Z4 a Z7 e o mastro. Cercas vivas voltam a tiquetaquear; postes da R6 e da R4; terminais com e-mails; L1 e L3 crescem; portão W3. **O T. rex sai pelo Passo Leste (T-REX-2), se o passo 3 ainda não o soltou** | bando B; o vapor mascara o som | 25 a 35 |
| O8 | Cartão nível 2 no escritório de segurança (linha B) | Z6, S16 | O5 | Portas internas dos labs; porta do abrigo do cume | bando A | 20 a 25 |
| O9 | Módulo do transmissor na sala dos servidores (linha B) | Z6, S15 | O8 | A peça que falta; portão W4. Desligar a sirene é opcional: acalma a área, mas tira o marco L4 | bando A pelo anel; eclusas | 15 a 25 |
| O10 | Diário da chefe dos tratadores no controle dos cercados (linha C) | Z5, S19 | O4 | A trilha dela até o abrigo dos saurópodes; as frequências de coleira do planalto; **a antena Yagi** | TRI-2; o par do bando A; o T. rex no Passo Leste (só ouvido antes do O7) | 10 a 15 |
| O11 | A última gravação dela, com o código do cume (linha C) | Z5, S22 | O10 | O código de acesso | saurópodes; o T. rex depois do O7 ou no passo 3 | 15 a 20 |
| O12 | O chamado: instalar o módulo, digitar o código, transmitir | Z7, S13 | O7, O9, O11 (e o cartão 2) | **Ato 4.** A resposta: "helicóptero na primeira luz, se a pista estiver acesa" | bando B no cume; o T. rex abaixo | 15 a 25 |
| O13 | Acender a pista e sobreviver até o resgate | Z5, S23 e S24 | O12 | Cena CE14 (seção 5.8). O farol e o balizamento acendem; amanhecer; helicóptero; fim | tudo; o diretor no máximo | 15 a 20 |
| | **Total principal** | | | | | **188 a 260** |

**O grafo de travas** (a auditoria do gerador testa se ele pode ser satisfeito em todos os estados de `reachability`, seção 7.3):

```
O1 → O2 → O3 ─┬─ O4 ─┬──────────────────────────── O10 → O11 ──┐
              │      └──┐                                       │
              │         ├─(O4 ou O5)─→ O6 ─┐                    ├─→ O12 → O13
              └─ O5 ────┤                  ├─→ O7 ──────────────┤
                        ├──────────────────┘                    │
                        └─→ O8 → O9 ────────────────────────────┘
```

**Observar antes de entrar (LV-3).** Cada local de objetivo tem 2 a 4 postes de luz sem sombra no caminho de patrulha dos
predadores, e janelas acesas como contraluz para silhuetas. Um ponto de observação fica a 25 a 35 m deles, atrás de cobertura dura.
Na névoa, a 60 m resta só 13,5 % do contraste, e com a lanterna apagada só os primeiros metros se leem [M, N1]: sem essas poças de
luz, a fase de observar e planejar não seria visual. O som também conta como observação: passos em grade metálica (x1,8) e o
receptor.

### 5.4 Atos, passos e relógio: uma noite em quatro atos (decisão 9)

O relógio avança de 30 a 50 minutos de jogo a cada objetivo principal, nunca em tempo real, então não existe falha por tempo. A
tensão, porém, sobe por **passo**, e cada linha concluída sobe um passo, em qualquer ordem. Assim o ato 2, que é de 48 a 58 % do
jogo principal (de 90 a 150 minutos [E, `horror.md` 6]), não fica num ajuste só. Os valores do diretor (Q, P e R) estão explicados
na seção 6.8, e estão também em `directorSteps` no JSON.

| Passo | Começa quando | Relógio | Diretor: Q / P / R | Chance de checar esconderijo | Comportamento novo | Territórios abertos | Teto até 150 m |
|---|---|---|---|---|---|---|---|
| 1 | Início (demo) | 21:00 a ~22:30 | 240 a 360 s / 60 a 90 s / 150 s | 0 | — | COMPY-1, DILO-03, PARA-2 (ouvida); raptors só no gancho | 2 predadores e 1 bando de compys |
| 2a | O3 concluído | ~22:30 em diante | 240 a 300 s / 60 a 90 s / 120 s | 0,25 | Checagem de esconderijo, com raptors em par | DEIN-A (até S19 e S20), DEIN-A-VILA, DEIN-B em pares; DILO-01 e 02; COMPY-2 a 4 | 2 e 1 |
| 2b | A primeira linha (O7, O9 ou O11) concluída | | 180 a 240 s / 60 a 90 s / 120 s | 0,25 | Trios sem flanco; adaptação às distrações, com aviso | os mesmos | 2 e 1 |
| 3 | A segunda linha concluída | ~01:30 a ~04:30 | 90 a 180 s / 60 a 90 s / 75 s | 0,5 | Flanco com aviso; o vento vira para 200 graus; T-REX-2 abre, se o O7 ainda não o abriu | Bandos completos; DEIN-A3 e DEIN-B3 | 3 e 1 |
| 4 | O12 concluído | ~04:30 até o amanhecer (~05:45) | 60 s / 30 s / 45 s | 0,6 | Convergência; brisa fraca do amanhecer | T-REX-3; todos convergem no cume e na rota da pista; COMPY-4 sai | 3 e 1 |

**Regras dos passos.**

- O T-REX-2 abre por **evento**: no O7 ou no início do passo 3, o que vier primeiro. Um jogador que faça a linha C primeiro ouve e
  cheira o T. rex no Passo Leste (cena CE10), mas só o encontra no planalto depois.
- Os comportamentos se desbloqueiam por passo, nunca pelas mortes do jogador, como em *Alien: Isolation*.
- **Medição na F4:** no máximo 12 minutos entre picos do diretor e nenhum passo com mais de 70 minutos.

**A lua (resolvendo uma diferença entre as pesquisas).** `facility.md` diz para nunca mover a lua, porque ela é bússola.
`open-world.md` a move entre os atos. A regra aqui: a lua fica no quadrante oeste a noite toda (rumo de 315 a 285 graus) e desce de
50 graus no ato 1 para uns 15 graus no ato 4. No ato 4 a luz pré-aurora sobe no **leste**, que é onde fica a pista: uma segunda
bússola que aponta para o resgate. É barato, porque é uma luz só.

### 5.5 Abrigos seguros

**Regras** (`facility.md` 4.5, `open-world.md` 3.3 e `horror.md` 17 e 18):

1. **Nenhum animal entra.** O NavMesh de nenhum tipo de agente tem caminho para dentro (auditoria, seção 7.3) e a porta fechada fica
   fechada. O diretor nunca manda um animal atrás do jogador que está num abrigo garantido.
2. **Primeiro é preciso garanti-lo**, e quase sempre em silêncio. O jogador fecha a porta e liga a **bateria** do abrigo. Só o SR1
   usa gerador (40 m de ruído, e grave: é o tutorial do ruído). Depois disso o abrigo fica seguro para sempre, com as exceções das
   regras 7 e 8.
3. **Um visor para a saída.** Cada abrigo tem janela, visor, periscópio ou monitor de câmera que cobre a saída de 20 a 30 m
   (`safeRooms.viewport` no JSON). Sair às cegas não acontece.
4. **O diretor limpa a porta.** Depois de 30 s com o jogador dentro, todo caçador a menos de 60 m do abrigo recebe a tarefa de ir
   para 100 m ou mais. Antes disso, um raptor em Search pode estar lá fora, e o visor mostra.
5. **Salvar, mapa e bandagem.** Cada abrigo tem um terminal de salvar, um quadro "você está aqui", bandagens e baterias para o
   receptor. Salvar leva 3 a 5 s e só é recusado durante uma perseguição (Chase) a menos de 40 m. Com a porta garantida nada entra,
   então recusar por compys, herbívoros ou Investigate só frustraria.
6. **O lado de fora continua audível**, baixinho. Seguro não é silencioso.
7. **Uma única invasão roteirizada** (cena CE13). Ela acontece na primeira entrada no SR5 a partir do passo 3. Se a linha B acabou
   antes e o jogador entrar primeiro no SR3, ela acontece no SR3; a geometria é feita nos dois. Nunca dispara ao carregar um save. O
   terminal desliga 60 s antes, com arranhões e um painel de ventilação de 1,5 m se soltando como aviso, e há uma fuga por uma segunda
   porta (para o corredor em anel, no SR5). Ela obedece a "raptors não abrem portas": eles entram pelo painel.
8. **O SR6 deixa de ser seguro no O13.** Ele é o save do ato 4, mas quando o O13 começa a janela do escritório cede e a espera é lá
   fora (cena CE14).
9. **Espaçamento.** De qualquer ponto das rotas principais, o abrigo mais próximo fica a no máximo **383 m** em linha reta, ou cerca
   de 6 a 8 minutos a 0,8 m/s (calculado no map-spec v2). O pior ponto é a Estrada da Usina, perto de (-239, -17). Isso só vale
   porque o SR4 agora abre no passo 2a: na v1 ele só abria depois do O7, e antes disso a R7 ficava a 651 m do abrigo mais próximo.

| Abrigo | Local | Ponto | Condição e como se garante | Visor | Vizinho mais próximo | Território em volta |
|---|---|---|---|---|---|---|
| SR1 | Estação de Campo | (-638, -57) | O3; gerador | janela para o pátio iluminado | 533 m (SR2) | — |
| SR2 | Enfermaria da Vila | (-232, -402) | porta e bateria | visor na porta, 25 m da rua | 415 m | DEIN-A-VILA, depois DEIN-A3; COMPY-2 |
| SR3 | Bunker de Comando (hub) | (125, -190) | porta blindada; bateria | periscópio sobre o pátio e o portão | 256 m | nenhum (fica fora dos territórios) |
| SR4 | Sala de Controle da Usina | (-182, 362) | desde o passo 2a; bateria | janela sobre o pátio | 269 m | DEIN-B |
| SR5 | Escritório de Quarentena | (372, -258) | porta estanque; bateria; a invasão do passo 3 | monitor de câmera do corredor | 256 m | DEIN-A |
| SR6 | Escritório do Hangar | (735, -170) | bateria; deixa de ser seguro no O13 | janela gradeada sobre a pista | 374 m | COMPY-4 até o passo 3 |
| SR7 | Abrigo do Cume | (82, 412) | cartão 2; bateria | visor, pátio do radome | 269 m | T-REX-3 no ato 4 |
| SR8 | Abrigo do Saurópode | (520, 330) | concreto; bateria | seteira, 30 m | 446 m | T-REX-2 e 3; BRACH |

O porto (S07) pode virar um 9º abrigo opcional se os playtests mostrarem falta de um na costa sul.

### 5.6 Conteúdo opcional e vinhetas de rota

**Opcional (3 itens, 10 a 20 min).**

| Item | Onde | O que dá |
|---|---|---|
| 1 | S07 Porto | O manifesto da evacuação (quem saiu, quem ficou) |
| 2 | S17 Clínica Veterinária | Notas de comportamento: um "manual" de cada espécie |
| 3 | S14 Ruína do P-01 | A história do T. rex e a coleira dele bipando "morte", dentro da caldeira, com alto risco |

**Vinhetas de rota.** Depois da demo, o deslocamento é a maior parte do jogo, e na v1 as rotas não tinham conteúdo além do repertório
do diretor. A v2 põe **uma vinheta a cada 150 m de rota principal, cerca de 39 em 5,9 km**, tiradas de um kit de 8 tipos: carcaça,
documento, cerca caída, luz piscando, veículo abandonado, travessia de manada (por spline), coleira no chão e rastro de sangue
(`rules.routeVignettes` no JSON). O gerador as põe por regra e um agente as ajusta à mão. **Medição na F4:** cada trecho é andado no
máximo 2,0 vezes numa partida.

### 5.7 Documentos

Regras de `facility.md` 4.8:

- **Cada documento carrega uma informação de jogo, ou fica ao lado de uma.** De 30 a 50 % trazem algo direto para o jogo: um código,
  uma rota, uma frequência de coleira, um comportamento, o lugar de um esconderijo ou de um abrigo. Os locais citados entram no mapa de
  papel.
- **São curtos**: até 120 palavras no papel.
- **Ficam onde aconteceu.** A sala mostra a consequência, e o documento dá a causa.
- **Seguem uma linha só**: a chefe dos tratadores, que se recusou a sair. A trilha dela vai da Estação de Campo ao abrigo dos
  saurópodes e é a linha C.
- **São escritos em português e inglês desde o primeiro dia.**

**Tipos de documento.** Fichas de animal (ID, espécie, frequência da coleira, comportamento), quadros de alimentação, relatórios de
incidente, ordens de quarentena, tíquetes de manutenção (os passos para religar a usina), o manifesto, bilhetes pessoais, fotos de
câmera impressas e e-mails, estes só depois do O7: a energia destrava a história.

**Quantidades.** A demo tem de 8 a 12 itens, **todos em texto**; o rádio é estática com legendas. O jogo completo tem de **40 a 60
itens**, mais ou menos um a cada 4 ou 5 minutos. Voz gravada só se a decisão 7 a pagar.

### 5.8 Cenas encenadas e o que se alcança em cada estado

**Cenas.** Cada espécie é apresentada numa cena num ponto que todas as rotas cruzam, ou o jogo usa a regra do primeiro contato
(seção 6.9). As cenas estão em `stagedScenes` no JSON, com ponto, gatilho e checkpoint. Uma cena força a materialização do animal e
se sobrepõe ao diretor só durante a cena.

| Id | Cena | Passo | Onde | O que acontece | Checkpoint antes |
|---|---|---|---|---|---|
| CE1 | O compy curioso | 1 | T2, perto do Deadfall | Um compy sozinho é atraído pelo ruído; inofensivo; ensina o arremesso | não |
| CE2 | A coleira no galpão | 1 | S03 | O receptor leva à coleira arrancada; o DL-03 se mostra: exibição, depois reposição | sim, na borda da grota |
| CE3 | A volta com o galão | 1 | S03 → S02 (239 m em linha reta) | O DL-03 espreita a volta: o clímax da demo | sim, ao pegar o galão |
| CE4 | Os compys na carcaça | 1 | T3, beira da falésia | Um bando come; há um objeto para jogar a menos de 5 m do caminho | não |
| CE5 | O gerador | 1 | S02 | O gerador (40 m) atrai o bando: o vão da Quonset ou o chute | sim, ao garantir o SR1 |
| CE6 | O gancho | 1 | S02, pela janela | O chamado de "predador" da manada; dois raptors cruzam o holofote, com brilho dos olhos a 1 m de altura. Sem perseguição | não |
| CE7 | O Passo Sul | 2a | pátio do hub | O T. rex segue o cheiro do jogador ao longo do portão a 10 a 20 m, bufa, perde o rastro na fumarola do passo e vai embora. Ensina cheiro, vento e quebra de rastro sem risco | sim, na entrada do pátio |
| CE8 | Par na vila | 2a | S05 | Um par cruza a luz L10 e revista a casa B (arrombada); a casa A (porta estreita) é segura | sim, na entrada de Z2 |
| CE9 | A mãe e o filhote | 2a | TRI-2, na R5 | Aviso de 1,5 s e carga contra um objeto atirado | sim, a 60 m |
| CE10 | O Passo Leste | 2a | T7 ou R5, a menos de 200 m do passo, antes do O7 | O T. rex bufa e faz os destroços rangerem; não sai | não |
| CE11 | Os saurópodes | 2a | rota dos saurópodes | Pisadas a 60 m, depois a travessia | não |
| CE12 | A manada do P-02 | 2a | T7 ou V3 | A manada vista pela primeira vez | não |
| CE13 | A invasão | 3 | SR5 (ou SR3) | Regra 7 da seção 5.5 | o terminal desliga 60 s antes |
| CE14 | O fim | 4 | S23 e S24 | Abaixo | em cada subpasso do O13 |
| CE15 | A coleira do P-01 | 2a | S14 (opcional) | O receptor leva à coleira arrancada do T. rex | sim |

**A cena final (CE14), passo a passo.** O jogador salva no SR6 e liga o gerador da pista no parque de combustível (S24): é ruído
grave, que o T. rex ouve a 200 m, e o SR6 deixa de ser seguro. O gerador engasga por volta de 60 s e de 120 s, e cada religada é uma
corrida exposta de uns 15 m. O farol e o balizamento acendem. O helicóptero só pousa depois que o jogador dispara o sinalizador da
plataforma, o que termina numa corrida de 40 m até ele. A explosão do parque de combustível é a única ferramenta grande para puxar o
T. rex para longe. Os bueiros sob a pista barram M, G e GG e funcionam como refúgio, porque os compys fogem do gerador e saem da
pista no ato 4.

**O que se alcança em cada estado** (`reachability` no JSON). Depois do O3, quase todo o exterior da ilha é alcançável: as travas
ficam nos prédios dos objetivos. É o mundo aberto que o dono pediu.

| Estado | Alcança, além do anterior |
|---|---|
| Passo 1 (demo) | Z1, menos a grota alta além da Cerca da Grota; a ponte da T8 está caída |
| Depois do O3 | Z2; o hub (cena CE7 na chegada); a bacia, pela porta de pessoal do Passo Sul; R4 até a cerca do complexo; R5 e o exterior de Z5 (passando pelo TRI-2); R8a; R6 até o corredor de Z3 e o pátio da usina (SR4); R7; o exterior do cume; T5 e T7 |
| Depois do O4 | T4 (Z1 para Z3), o complexo da bomba, o pátio dos labs, S19, os cercados |
| Depois do O5 | A casa de bombas, o salão das turbinas, as portas externas dos labs |
| Depois do O7 | O portão W3; o T. rex no planalto; os postes da R6 e da R4; as cercas vivas |
| Depois do O8 | O interior dos labs e o SR7 |
| Depois do O9 | O portão W4 |
| Depois do O12 | Ato 4: T-REX-3 na rota cume, T5, T7, R8a, pista |

**Auditoria das apresentações.** Para cada espécie, toda rota até o primeiro território dela passa pela cena dela, ou a espécie está
na regra do primeiro contato. Resultado no mapa v2:

| Espécie | Cena | Rotas que pulam a cena | Cobertura |
|---|---|---|---|
| Compsognathus | CE1 | nenhuma | — |
| Dilophosaurus | CE2 e CE3 | nenhuma | — |
| Deinonychus | CE6 e CE8 | nenhuma (O4 e O5 ficam na vila) | — |
| T. rex | CE7 | a linha A pela T4 sem passar no hub; a descida do cume pela T6b | primeiro contato (a mochila) e o bufo de cheiro a 150 a 200 m |
| Triceratops | CE9 | quem chega a Z5 pelo cume (T5 e T7) | primeiro contato (derruba) |
| Parasaurolophus e Brachiosaurus | chamados na demo, CE11, CE12 | qualquer uma | não matam |

---

## 6. Furtividade e ameaça

### 6.1 Tipos de esconderijo e classes de tamanho

| Tipo | Como funciona no código | Situação |
|---|---|---|
| Cobertura dura | Bloqueia o linecast de visão. A de pé tem 1,8 m ou mais; a de agachado, 1,1 m ou mais | A floresta só tem a de agachado; os prédios e props dão a de pé |
| Ocultação | Esconde do olho, mas não da física: samambaia alta, moitas, escuro, vapor | **Regra nova.** Dentro de um volume de ocultação, o tempo de detecção é multiplicado por 3 e a visão fica limitada a 8 m em pé e 4 m agachado ([D], NÃO VERIFICADO). Hoje uma moita só tem cápsulas de tronco como colisores (spec 7.4) |
| Esconderijo | Interação `HidingSpot`: o jogador é teleportado para dentro | **Existe** (greybox). Hoje é absoluto: só é visto se o predador o viu entrar numa perseguição, nos últimos 0,5 s (`RaptorAI.cs` linhas 141, 179 e 180) [M]. A v2 acrescenta a checagem abaixo |
| Refúgio | Lugar onde os grandes não chegam e os pequenos sim | Precisa das classes de tamanho abaixo |
| Abrigo seguro | Nenhuma IA entra; ponto de salvar | Novo (seção 5.5) |

**A checagem de esconderijo.** Na v1, quebrar a linha de visão por 0,5 s, entrar em qualquer esconderijo e esperar no máximo 90 s
vencia todo predador: a LV-2 põe um esconderijo a até 16 m de cobertura que fica a até 8 m de toda rota, e o diretor sempre retira o
animal depois do pico. *Alien: Isolation* usou o mesmo diretor, mas o alien podia checar um armário: uma prévia descreve o pedido para
controlar a respiração enquanto ele fareja o armário ([Shacknews](https://shacknews.com/article/83607/alien-isolation-preview-extra-terrifying))
[V]. Jogadores relatam que ele checa mais quando o jogador abusa dos armários
([fórum da Steam](https://steamcommunity.com/app/214490/discussions/0/3109144584165201158); observação da comunidade, NÃO VERIFICADO como
regra do jogo). A regra aqui [D]:

- **Quando pode acontecer.** Só em Search ou Investigate, só quando o ponto de busca está a até 4 m da saída (`ExitPoint`) de um
  esconderijo, e só com um **motivo**: um ruído a até 4 m do esconderijo nos últimos 10 s; o cheiro do jogador a até 10 m a favor do
  vento (raptors, T. rex); ou o jogador visto a até 20 m nos últimos 20 s.
- **Chance, por passo:** 0 no passo 1, 0,25 nos passos 2a e 2b, 0,5 no 3 e 0,6 no 4. Um esconderijo usado duas vezes em 10 minutos no
  mesmo território é checado primeiro.
- **Quem checa.** Raptors em qualquer esconderijo que a classe M alcança. O T. rex só onde não há teto duro (camas de samambaia, sob
  troncos). Compys não checam: eles arrastam o jogador para fora (classe P).
- **O aviso.** De 2 a 3 s de farejar audível e de aproximação, visível pelas frestas do esconderijo.
- **A resposta.** Prender a respiração (seção 6.10) por no máximo 6 s, parado. Se passar disso, um arquejo faz ruído de 6 m. Falhar
  quer dizer morte (raptor, T. rex).
- **O jogador escondido continua cheirando.** É isso que faz o vento importar também para raptors e compys.

**Classes de tamanho, iguais aos tipos de agente do NavMesh** (seção 7.5). As larguras saem do bake, não do raio, e as do v1 não
fechavam: um agente de raio 0,25 m tem 0,5 m de largura e não passa num vão de 0,3 m, e um de raio 0,5 m, depois da erosão, deixa uma
faixa de 0,2 m num vão de 1,2 m, menos de 1 voxel [`technical.md` 6].

| Classe | Espécies | Agente (raio / altura) | Voxel | Passa por | É barrada por |
|---|---|---|---|---|---|
| P | Compsognathus | 0,25 / 0,5 m | 0,10 m | **Links gerados** (`NavMeshLink`) nos pontos de aperto: dutos, vãos sob contêineres em estacas, janelas quebradas. Nunca por vãos de voxel | qualquer abertura sem link |
| M | Deinonychus | 0,5 / 1,8 m (o tipo atual) | 0,167 m | Aberturas de **1,5 m ou mais** (portas duplas, de enrolar, paredes quebradas, janelas baixas quebradas) | **0,9 m ou menos** (porta simples), aberta ou fechada. Entre 0,9 e 1,5 m é proibido nas instalações |
| G | Dilophosaurus | 1,0 / 2,5 m | 0,25 m | Mata densa por onde os caules, **contando os finos**, deixam passar; nenhum prédio | moita de abetos jovens; qualquer porta |
| GG | T. rex, Triceratops | 2,0 / 5 m | 0,33 m | Só ao ar livre, por **faixas de 6 m ou mais**, com raio de curva de 15 m ou mais | vãos de **menos de 4 m**; concreto; cerca viva; os portões dos passos. Quebra galpões e cercas mortas |
| — | Parasaurolophus, Brachiosaurus | spline | — | Seguem a rota e as faixas de fuga | — |

**Os pequenos expulsam.** Um vão sob um contêiner barra M, G e GG, mas não P. O guincho dos compys é um evento de ruído que traz os
raptors. Isso liga a ameaça leve à pesada de forma sistêmica.

### 6.2 Esconderijos por bioma

| Bioma | Cobertura dura | Ocultação | Esconderijos | Refúgios (classes barradas) | Chão barulhento |
|---|---|---|---|---|---|
| B1 Mata | Campo de matacões (picos de 1,8 a 2,0 m, poucos), montes de raiz, barrancos | Moitas de abetos jovens (uns 30 % do núcleo), samambaias (26 % de cobertura) | Troncos ocos, cavas sob placas de raiz, a cova das samambaias (1,1 m) | Túnel de tronco caído (barra M, G e GG; o jogador rasteja); moitas (barram G e GG) | Galhos secos (309 colocados), sulcos de cascalho |
| B2 Grota | Margens, matacões | Samambaias arborescentes, samambaia-rei | Sob margens escavadas, entre raízes | O leito de 3 a 10 m (barra GG) | Seixos, respingo |
| B3 Savana | Kopjes | Samambaia alta de 1,2 a 2 m | Fendas dos kopjes | Ilhas de abetos na caldeira (barram GG) | Pedras |
| B4 Queimada | Placas de raiz, troncos caídos | Rebrota | Sob troncos caídos | Pilhas de troncos (barram GG) | Carvão, galhos (1,4 a 1,6) |
| B5 Basalto | Saliências, blocos de lava, tubulações | **Fumarolas** (ocultação, máscara de ruído e quebra de rastro) | Carcaças de separadores | Fendas estreitas (barram G e GG; o jogador rasteja) | Cascalho (1,6) |
| B6 Charneca | Matacões | Arbustos de 1,9 a 3,2 m | Entre matacões | — | Cascalho |
| B7 Costa | Falésias, cascos de barco, rochas | Capim costeiro, borrifo noturno (banco de névoa) | Sob o píer (passarela sobre pedras, sem nadar), barcos virados | Bueiros sob a pista (barram M, G e GG; o jogador rasteja) | Cascalho de praia (alto), tábuas do píer |
| B8 Instalações | Paredes, contêineres, veículos, geradores, racks de tubos | Grama crescida (não esconde em pé) | Armários, sob camas, vãos sob contêineres | Vãos sob contêineres (só P entra); torres (barram GG); portas estreitas (barram M) | Grade metálica (1,8), vidro e policarbonato quebrados (muito alto) |

### 6.3 Esconderijos e acesso de raptor por prédio

A regra de largura da seção 6.1 faz de toda sala com uma porta estreita uma sala segura contra raptor. Isso fica, **de propósito**:
"porta estreita é segura contra raptor, mas não contra compy" é uma regra que o jogador aprende. Mas cada prédio onde a v1 punha
raptors "por dentro" precisa de aberturas largas, e esta tabela diz quais. Ela também está em `sites.raptorAccess` no JSON.

| Prédio | Esconderijos | Refúgio | Laço (para contornar um predador) | Acesso de raptor (1,5 m ou mais) | Regra especial |
|---|---|---|---|---|---|
| Estação de Campo (S02) | Armário, sob o beliche, vão sob a Quonset | Torre | Em volta da Quonset (uns 45 m de perímetro) | porta dupla na ponta da Quonset, fechada depois do O3 | Dar partida no gerador é um ruído de 40 m [D] |
| Vila: casa A | Guarda-roupa, sob a cama | A própria casa | Porta da frente e janela dos fundos (só o jogador passa) | **nenhum**: porta estreita intacta | A casa que ensina a regra (cena CE8) |
| Vila: casa B | Guarda-roupa, banheiro | Vão sob a casa em estacas (só P segue) | Porta da frente e parede arrombada | parede arrombada de 1,5 m, com batente arranhado | O par a revista na cena CE8 |
| Vila: posto de segurança (O5) | Sob a mesa, armário de armas vazio | — | Balcão e porta dos fundos | porta dupla | — |
| Enfermaria (SR2) | — | É o abrigo | — | porta de aço | Abrigo |
| Bunker do hub (S08, SR3) | — | É o abrigo | Pátio em volta do bunker | o pátio é aberto; o bunker tem porta blindada | O portão do Passo Sul fica a 10 m da cerca norte (cena CE7) |
| Laboratórios (S15) | Armários de bancada, gabinetes de capela, racks de servidor | Dutos de ventilação (só P, por links) | Corredor em anel em volta do núcleo (40 a 60 m) | **o anel tem portas duplas**; as eclusas (0,9 m) não deixam passar | Eclusas de quarentena abrem uma porta por vez: uma travessia lenta e tensa. Os chuveiros de descontaminação fazem barulho ao ligar. A sala dos servidores fica atrás de uma eclusa |
| Incubação (dentro do S15) | Entre as fileiras de incubadoras | — | De fileira em fileira | pelo anel | Quente e úmido: o facho espalha numa névoa local |
| Casa dos Carnívoros (S18) | Corredor de tratador atrás da malha | O próprio corredor, com as portas fechadas | Corredor e laço das tocas | nenhum no corredor (portas estreitas); as baias são a toca | A malha barra corpos, não a visão |
| Clínica (S17, opcional) | Caixas de transporte de animais, cabine de raio X | — | Laço da sala de tratamento | porta dupla | A sala de necropsia é um quadro de história |
| Escritório de Segurança (S16), Controle dos Cercados (S19), Casa de bombas (S11) | Sob a mesa, armário | — | Porta e janela | janela baixa quebrada de 1,5 m | Uma sala cada |
| Usina (S12) | Galerias de tubos, sob as passarelas do salão das turbinas | Sala de controle (SR4) | Passarela sobre o salão das turbinas | porta de enrolar de 4 m no salão | **Zona barulhenta**: a menos de 15 m de uma turbina, os passos do jogador e a audição dos animais caem para 0,5 vez. Os dois lados ouvem menos, o que é justo |
| Estação do Cume (S13) | Cofre de cabos | Abrigo do equipamento (SR7) | Nenhum | pátio aberto | A aproximação final não tem cobertura: é só questão de tempo |
| Hangar (S23) | Entre os tambores, sob a bancada | Escritório (SR6) até o O13 | Em volta do avião desmontado | o hangar é aberto (até GG entra) | Cena CE14 |

**Linguagem visual das aberturas.** Batente arranhado, porta arrancada e mancha de arrasto no chão: raptor passa. Porta estreita
inteira: raptor não passa. Um raptor parado do lado de fora de uma porta estreita espera ali até o diretor o puxar para longe; o
jogador o vê pela fresta.

Todas as portas duplas da instalação têm visor, então o jogador pode olhar antes de entrar, como fazem os tratadores. **Não encenar
uma caçada na cozinha** (4.9), em nenhum prédio.

### 6.4 Regras de linha de visão (LV) e tempo até a captura

**Tempo até a captura** [D]. O sprint do jogador é de 6 m/s, sem fôlego, e continua assim: é mais simples ajustar a velocidade dos
predadores. Cada espécie tem uma regra de distância até o refúgio que dá ao jogador uma saída lendo o terreno.

| Espécie | Perseguição | Tempo até a captura | Refúgio |
|---|---|---|---|
| Deinonychus | 9 m/s; ganha 3 m/s do jogador; ataque a 1,8 m | (d - 1,8)/3 s: a 10 m, 2,7 s; a 16,8 m (um jogador agachado visto no limite), 5,0 s; a 28 m, 8,7 s | cobertura dura a até 8 m de toda rota (LV-1) e um esconderijo a até 16 m dela (LV-2); uma porta estreita |
| T. rex | 8 m/s depois de 2 s de aceleração; curvas de no máximo 45 graus por segundo | de 20 m, cerca de 14 s (84 m de corrida); de 30 m, cerca de 19 s (114 m) | algo que barre GG (ilha de abetos, vão de menos de 4 m, prédio) a até **60 m** de todo ponto de rota no território dele (`rules.refugeSpacing`) |
| Dilophosaurus | bote de 10 m/s até 12 m (o aviso de 1,5 s é a resposta); depois de errar, 1,5 s parado e perseguição de 6,5 m/s por 10 s | livre a 6 m/s: a distância cresce para uns 11 m e se fecha a 0,5 m/s, em 22 s, então ele desiste antes; com o galão (5 m/s): pega em uns 6 s, a menos que o jogador largue o galão | algo que barre G (moita de abetos, prédio) a até 20 m |
| Triceratops | carga de 7,5 m/s por até 40 m, depois do aviso de 1,5 s; curvas de no máximo 30 graus por segundo na carga | só acerta quem fica na linha da carga; um passo de 3 m para o lado no último segundo a evita | qualquer tronco com mais de 40 cm, ou rocha |
| Compsognathus | arrancadas de 7 m/s por no máximo 3 s | não mata; morde e reagrupa | subir em rocha ou veículo; porta fechada |
| Parasaurolophus | estouro de 8 m/s pelas faixas de fuga, depois de 2 a 3 s de ronco | só derruba quem está na faixa | agachar atrás de cobertura de 1 m ou mais (é contornado) |

Esconder-se só funciona se o jogador entrar mais de 0,5 s depois de ter sido visto pela última vez, e pode ser checado (seção 6.1).

| # | Regra | Valor |
|---|---|---|
| LV-1 | Em territórios de raptor, cobertura dura que quebre a linha de visão a partir de qualquer ponto de rota principal | a até 8 m |
| LV-2 | Dessa cobertura, um esconderijo | a até 16 m |
| LV-3 | Toda área de objetivo vigiada pode ser observada: 2 a 4 postes sem sombra no caminho de patrulha, janelas acesas como contraluz, e um ponto de observação atrás de cobertura dura | ponto a 25 a 35 m dos postes |
| LV-4 | Nenhuma corrida aberta em zona de predador sem cobertura ou ocultação em pelo menos um lado. Trechos maiores (a pista, o planalto) são travessias de propósito, com janela de tempo | 25 m ou menos |
| LV-5 | Interiores: nenhum corredor reto sem curva, porta ou nicho; todo cômodo tem duas saídas, exceto abrigos e armadilhas sinalizadas | 12 m ou menos |
| LV-6 | Laços em volta de um bloco central, um por prédio de 300 m² ou mais | 25 a 60 m de perímetro |
| LV-7 | Olhar antes: janela, fresta ou visor em cada cômodo de rota principal e na saída de cada abrigo | 1 por cômodo |
| LV-8 | Vertical: torre ou telhado acima da vegetação, com uma saída só e fora do alcance de GG | 12 a 20 m |
| LV-9 | Poças de luz (holofotes e postes acesos): visão de pé e agachado a 28 m inteiros; detecção x2 lá dentro | [D], NÃO VERIFICADO |
| LV-10 | Lanterna | seção 6.6 |
| LV-11 | Refúgio contra T. rex e Dilophosaurus | tabela acima |

Essas regras são checadas na auditoria do gerador e nas capturas de cada zona. Por exemplo: um raycast de cada ponto de rota a cada
5 m procura cobertura dura a até 8 m, e um `NavMesh.Raycast` do tipo GG procura um refúgio a até 60 m.

### 6.5 Sinais de perigo

A regra é: **ouça antes de ser visto, veja antes de ser pego, pague por cada olhada.**

| Faixa | Sinal | Base real |
|---|---|---|
| Acima de 200 m | Alarmes de Parasaurolophus; o ronco de um estouro; o rugido do ápice (racionado); a sirene dos labs; aves levantando da copa | Cerca de 70 espécies de vertebrados escutam os alarmes de outras espécies ([Magrath et al. 2015](https://ro.uow.edu.au/smhpapers/2748)). Um modelo da crista dá "um ronco grave ressonante que muda de altura" ([Sandia 1997](https://www.sandia.gov/labnews/1997/12/19/dinosaur-story/)) |
| 50 a 200 m | O bufo de cheiro do T. rex (150 a 200 m); o chamado de batedor dos raptors (120 m); tagarelice de compys; as pisadas do T. rex (100 m); o receptor começa a bipar | — |
| 15 a 50 m | **Os avisos de patrulha dos raptors (25 a 30 m) e o chilro de desconfiança (40 m)**; a bolha de silêncio; galhos estalando; mudas se inclinando e voltando (já existe, W1); **brilho dos olhos no facho**; o bipe mais forte | A bolha de silêncio tem base fraca ([Ethology 2017](https://bio.acousti.ca/node/52564)): é convenção, não ciência, e por isso não é o único sinal da faixa. O brilho dos olhos de crocodilianos aparece em levantamentos com holofote ([UF/IFAS](https://edis.ifas.ufl.edu/publication/UW198)); em dinossauros é licença artística |
| Abaixo de 15 m | Pisadas, respiração, garras no concreto ou no metal; o preparo do ataque | — |

**Matriz de avisos por espécie e estado** [D]. Os alcances são de audição para o jogador, com o áudio espacializado. Para cada
predador, o aviso de patrulha se ouve de mais longe do que ele vê um jogador agachado com a lanterna em Baixa: é isso que sustenta
"ouça antes de ser visto".

| Espécie | Patrulha | Desconfiado | Caçando | Preparo do ataque |
|---|---|---|---|---|
| Deinonychus | estalos de garra e de respiração, 25 a 30 m | chilro baixo, 40 m | chamado de batedor, 120 m; reúne o bando em 8 s | pelo menos 1,0 s: agacha, sibila, abaixa a cabeça; 15 m |
| Dilophosaurus | passos na samambaia (`Rustled`), 15 m; parado na emboscada, nada | congela; estalo de garganta, 12 m | frondes se mexendo à frente do jogador, 10 m | com luz: exibição com silvo grave, 1,5 s; sem luz: silvo seco, 1,0 s, audível a 10 m |
| T. rex | pisadas sentidas a 100 m; respiração a 30 m | **bufo longo e grave ao pegar o cheiro, 150 a 200 m** | pisadas acelerando; ronco, 60 m | 1,5 s: cabeça baixa e rugido curto |
| Compsognathus | tagarelice, 20 m | chilro, 15 m | guinchos em coro, 25 m (é o evento de ruído) | chiado de 0,5 s antes da mordiscada |
| Triceratops | mastigar e bufar, 20 m | cabeça erguida, bufo, 25 m | — | 1,5 s: cabeça baixa e pata raspando, 20 m |
| Parasaurolophus | grunhidos de manada, 60 m | cabeças erguidas | "desconhecido": três chamados curtos; "predador": um longo; 300 m | estouro: ronco do chão 2 a 3 s antes |
| Brachiosaurus | pisadas, 60 m | — | — | — |

**A regra de justiça.** Nenhuma morte vinda de fora do campo de visão de 90 graus do jogador, a menos que o atacante tenha feito um
aviso espacializado nos últimos 2 s e pelo menos 1,0 s antes do contato. Quem flanqueia vocaliza ao começar a fechar.

**A altura e o espaçamento dos olhos dizem a espécie**, de graça, com o olho emissivo: compy a uns 0,25 m, raptor a 1 m,
Dilophosaurus a 1,7 m, Triceratops baixo e largo, T. rex a 3,5 a 4 m.

**Teste antes de fechar a F3:** um teste cego de áudio, em que os testadores dizem a espécie e o estado só de ouvido. Passa com 80 %
de acertos ou mais.

**O receptor de telemetria VHF** (decisão 8). É o detector de movimento de *Alien: Isolation* tornado real, mas com lacunas.

- **Como funciona de verdade.** Coleiras reais bipam num ritmo fixo e o dobram depois de horas sem movimento (sinal de morte). Uma
  coleira tocava 40 pulsos por minuto em repouso, 60 ativa e 80 depois de 2,5 h parada
  ([ADF&G](https://www.adfg.alaska.gov/static/home/library/pdfs/wildlife/research_pdfs/alces/5197.pdf)) [V]. A direção se acha com
  uma antena direcional Yagi de mão
  ([Perdix](https://perdixwildlifesupplies.com/products/perdix-vhf-yagi-antenna)) [V], varrendo até o pico do sinal
  ([relato informal](https://forums.radioreference.com/threads/directional-tracking.247573/), NÃO VERIFICADO num manual de campo).
- **No jogo, em duas etapas.**
  - **Antena de chicote** (desde a demo): o jogador sintoniza a frequência impressa na ficha de um animal (1 s para trocar). O volume
    dá a faixa de distância (longe, médio, perto) e o ritmo dá o estado (repouso, ativo, morte). Nenhuma direção.
  - **Antena Yagi**, achada na S19 como recompensa da linha C: dá a direção por uma varredura de 2 a 3 s. Durante a varredura a
    lanterna fica em Baixa e o alto-falante faz ruído de 3 m.
- **Alcances de jogo** [D, NÃO VERIFICADO]: 150 m em campo aberto, 60 m pela mata, 20 m pelo concreto.
- **O alto-falante faz ruído** (3 m de raio). O modo fone esconde o ruído, mas desliga o LED.
- **Na demo**, o receptor está com o tratador morto perto da cova das samambaias, junto da ficha do DL-03. O bipe leva à coleira
  arrancada, no galpão.

**Quem tem coleira** (`collars` no JSON):

| Animal | Coleira | Nota |
|---|---|---|
| DL-01, DL-02 | sim | frequências nas fichas de Z3 |
| DL-03 | arrancada | no galpão S03, bipando o ritmo de morte |
| TRI-1; TRI-2 (a mãe) | sim | o filhote nasceu solto |
| Manadas de Parasaurolophus | 2 de 6 (PARA-1); 1 de 4 (PARA-2) | informação |
| Brachiosaurus | os 2 | informação |
| Bando A | 2 de 3 | o terceiro nasceu solto: o receptor nunca mostra o bando inteiro |
| Bando B | nenhum | nasceram no tubo de lava |
| T. rex | arrancada | bipa "morte" na S14: uma pista falsa |
| Compys | nenhum | |

### 6.6 A lanterna: três estados (regras T1 a T3; decisão 8)

Os ganchos `PlayerFlashlight.IsOn` e `IsOnChanged` existem, mas nada os lê ainda; o alcance atual é de 35 m
(`PlayerFlashlight.cs` linha 32) [M, N1 seção 7].

**Por que três estados.** Na v1 a lanterna só ligava ou desligava, e a v1 mandava deixá-la ligada sob a copa (B1, B2), onde vive
a maioria dos predadores. Ligada, um raptor via o facho a 80 m e o jogador via olhos a uns 50 m; apagada, a imagem fica quase preta
(médias de 0,03 a 0,08 [M, N1]) e um raptor ainda via um jogador de pé a 14 m. O jogador era visto primeiro nos dois casos. Lanternas
reais têm modos alto e baixo, então o jogo também tem:

| Estado | Luz | Legível até | Troca |
|---|---|---|---|
| **Alta** | 300 lm, alcance de 35 m | ~30 m | Alta ↔ Baixa sem queda de exposição |
| **Baixa** | ~30 lm | 8 a 10 m | o hábito na mata |
| **Apagada** | — | os primeiros metros | Apagar custa 2 a 3 s de adaptação da exposição [M, STATE] |

**Quem vê o quê, à noite** [D]. Esta tabela substitui as duas da v1 (a regra T1 dizia 60 m e a tabela 4.2 dizia 80 m para o raptor;
fica 60 m).

| Espécie | Alta: facho e mancha / brilho pela folhagem | Baixa: facho | Apagada: jogador de pé / agachado | Reação |
|---|---|---|---|---|
| Deinonychus | 60 m / 40 m | 20 m | 14 m / 8,4 m | investiga a mancha (T2) |
| Compsognathus | 30 m / — | 10 m | 8 m / 5 m | atraído além de 10 m; Alta em cheio a menos de 5 m o espalha e faz guinchar |
| Dilophosaurus | 25 m / — | 12 m | 12 m / 7 m | além de 12 m, congela e se reposiciona; a menos de 12 m, exibição |
| Triceratops | 20 m / — | 10 m | 10 m / 6 m | facho na cara a menos de 15 m: aviso e carga |
| Parasaurolophus | 60 m / — | 20 m | 25 m / 15 m | facho na cara a menos de 15 m: alarme de "desconhecido" |
| T. rex | 30 m (a lâmpada) / — | 10 m | 15 m / 8 m | pouca; ao amanhecer vê a 100 m em qualquer estado |

- **T1.** A lâmpada vista de frente (o jogador dentro do cone do animal, com o facho a até 30 graus dele) conta como "facho" na tabela
  acima, com detecção x2.
- **T2.** Uma mancha iluminada numa superfície, dentro do cone de um animal e dentro da distância da tabela, é um estímulo. O animal
  vai até a **mancha**, não até o jogador. A lanterna também serve de isca.
- **T3.** Com a lanterna apagada à noite, valem as distâncias da coluna "Apagada". Sem isso, a noite não significa nada para a IA.
- **O brilho dos olhos** aparece para o jogador a uns 50 m com a lanterna em Alta e a 12 m em Baixa, só quando o facho cruza os olhos.
  Os 50 m passam do alcance de 35 m da lanterna: o material do olho calcula a emissão pelo ângulo e pelo estado da lanterna, não pela
  luz recebida. É um truque de propósito (seção 7.6). **Quando você vê olhos virados para você em Alta, eles já viram a sua luz.**
- **A lanterna nunca acaba.** O custo da luz é a exposição. Uma bateria que acaba faria o jogador andar no escuro para economizar,
  o que é chato. As baterias dos abrigos são para o receptor.
- **O padrão de cada bioma:** sob a copa (B1, B2), Baixa é o hábito e Alta é uma olhada paga. No aberto (B3, B5, B7), o luar basta e
  qualquer lanterna denuncia de longe. Sair da mata para a savana troca o hábito do jogador.
- **Carregando o galão**, a lanterna fica presa no peito, só em Baixa (seção 6.10).

### 6.7 Ruído e máscaras

| Fonte | Raio ou efeito | Origem |
|---|---|---|
| Passo agachado, andando, correndo | 2, 8, 18 m, vezes o multiplicador do chão (seção 3.2) | [M] `PlayerController.cs` |
| Rastejar | 1 m | [D] |
| Objeto atirado | 24 m | [M] `Throwable.cs` |
| Chute | 6 m | [D] |
| Galão chacoalhando | 6 m andando, 12 m correndo; largar o galão faz 12 m | [D] |
| Escada | 8 m enquanto sobe | [D] |
| Arquejo depois de prender a respiração | 6 m | [D] |
| Partida de gerador | 40 m; também é ruído **grave** (o T. rex ouve a 200 m) | [D] |
| Guincho de compy | 25 m | [D] |
| Manada assustada | 30 m na manada; alarme a 300 m | [D] |
| Alto-falante do receptor | 3 m | [D] |
| Máscara: arrebentação | ruído x0,5 a até 30 m das ondas | [E] |
| Máscara: turbina | jogador e audição dos animais x0,5 a até 15 m | [D] |
| Máscara: saurópode andando | jogador x0,5 a até 25 m | [D] |
| Máscara: cerca viva | o tique esconde passos a até 3 m | [D] |
| Máscara: vento nas cristas e fumarolas | x0,6 a 0,8 | [D] |

O `NoiseSystem` já é uma lista estática global (`Core/NoiseSystem.cs`), então funciona entre cenas [M]. Ele precisa ganhar o tipo
"grave" e as zonas de máscara. O eco do túnel saiu com o túnel.

### 6.8 As regras do diretor de IA

O desenho é de `open-world.md` 3.3, com as regras de `facility.md` 4.1, `species.md` 4.3 e `horror.md` 11 e 12. Os números são
valores iniciais [D], ajustados nos testes.

**O que ele lê, 4 vezes por segundo, em `Island.unity`:**

- de cada predador materializado: distância, estado e `Awareness`, que já são públicos em `RaptorAI.cs` [M];
- se o jogador vê ou ouve o predador (linha de visão dentro do cone da lanterna, avisos audíveis da tabela 6.5);
- do jogador: se está escondido, o estado da lanterna, o estado de ferimento e o raio de ruído;
- o tempo desde a última perseguição e o passo atual.

**A tensão T** vai de 0 a 1:

- sobe 0,15 por segundo com um caçador a até 15 m;
- sobe 0,05 por segundo de 15 a 40 m;
- sobe 0,10 por segundo quando o jogador vê ou ouve o predador;
- sobe 0,30 por segundo durante uma perseguição;
- fora isso, cai 0,03 por segundo.

**Estados:**

| Estado | Regra |
|---|---|
| Quieto | T abaixo de 0,2. Depois de Q segundos de calma (tabela 5.4), o diretor escolhe o **caçador** mais próximo, dentro do território atual dele, e lhe dá uma tarefa: "investigar um ponto de 40 a 60 m do jogador, contra o vento" |
| Crescendo | A criatura se aproxima pelos próprios sentidos. Eventos ambientes de um repertório tocam com intervalo de 45 a 90 s: um bufo distante, compys nas samambaias, alarmes de herbívoros, uma luz piscando, uma carcaça |
| Pico | T acima de 0,8, ou uma perseguição. Dura no máximo P segundos e depois o caçador recebe a tarefa de **recuar** para um ponto a 150 m ou mais, fora de vista, a menos que esteja perseguindo o jogador à vista |
| Alívio | No mínimo R segundos sem ameaça mandada pelo diretor. Coletáveis, documentos e mapas são colocados para preencher esse tempo |

**Papéis por espécie** (`territories.directorRole` no JSON). Mandar e recuar não servem para todos: os territórios do Dilophosaurus
têm 1,6 a 2 ha (raio equivalente de 72 a 80 m) e os do Triceratops 0,4 a 0,5 ha (35 a 40 m), então um recuo de 150 m os tiraria do
território, e mandar o emboscador atrás do jogador quebraria a previsibilidade que o põe no nível 2.

| Papel | Espécies | O que o diretor faz |
|---|---|---|
| Caçador | raptors, T. rex | Manda e recua, como acima |
| Perigo fixo | Dilophosaurus, Triceratops | Nunca manda. Só sobe ou baixa o alerta. No Pico, eles voltam à toca ou ao ponto de alimentação por R segundos |
| Pressão | compys | Pode mandar para qualquer ponto dos territórios COMPY. Não contam para o Pico, a menos que haja um caçador a até 80 m |
| Manada | Parasaurolophus, Brachiosaurus | Nunca manda; os alarmes e os estouros são eventos do repertório |

**Regras fixas:**

1. O diretor manda para a **área geral**, nunca para o ponto exato. A criatura acha o jogador sozinha.
2. **Nunca nasce nem se teletransporta um animal à vista.** Criaturas fora de cena existem em duas camadas (seção 7.5) e o diretor as
   promove ou rebaixa.
3. **Tetos.** Até 150 m: 2 predadores e 1 bando de compys nos passos 1 a 2b; 3 e 1 nos passos 3 e 4. **A até 80 m do jogador, duas
   vagas com nome:**
   - **vaga de caçador:** 1 bando de raptors (até 3), **ou** 1 Dilophosaurus, **ou** o T. rex;
   - **vaga de pressão:** 1 bando de compys;
   - herbívoros sem teto.

   O pior caso desenhado é então 3 raptors e 6 compys, que é o encontro pesado da seção 7.6, e a corrente compy-raptor cabe: o
   guincho vai a 25 m, a audição do raptor é x1,5 (uns 38 m), e os compys seguem o jogador a 6 a 15 m.
4. **Ritmo.** Em média, no máximo uma perseguição a cada 8 a 10 minutos.
5. **Nunca contra um jogador dentro de um abrigo garantido** (exceto a invasão CE13). Depois de 30 s com o jogador dentro, todo caçador
   a menos de 60 m vai para 100 m ou mais.
6. **Depois de carregar um save**, o diretor começa em Alívio por R segundos, e a criatura que matou o jogador vai para 100 m ou mais.
   Ninguém recarrega dentro da mesma perseguição.
7. **As cenas encenadas** (seção 5.8) se sobrepõem ao diretor só enquanto duram.
8. **Os herbívoros nunca caçam.** Os estouros e os alarmes deles avisam que há predadores por perto.
9. **O que se mede:** a quietude nunca passa de Q; pelo menos 2 picos por partida da demo; na F4, no máximo 12 minutos entre picos e
   nenhum passo com mais de 70 minutos.

### 6.9 Morte e ferimento

A v1 não dizia o que acontece na morte. Hoje a morte recarrega a cena inteira (`GameSession.KillPlayer`, linhas 34, 35 e 38) [M], e
os objetivos ficam de 15 a 35 minutos um do outro: uma morte no terceiro disjuntor do O7 podia custar o objetivo inteiro. *Alien:
Isolation* foi criticado por perder "até quinze minutos de progresso" e por deixar o jogador vulnerável ao salvar
([VGChartz](https://www.vgchartz.com/game/77802/alien-isolation/reviews/254064/)) [V].

**Quem mata e quem fere** [D]:

| Fonte | Efeito |
|---|---|
| Raptor (ataque) | morte, com câmera de morte |
| T. rex | morte; **o primeiro contato do jogo** morde a mochila e derruba (Sangrando) |
| Dilophosaurus | **o primeiro bote do jogo** derruba (Sangrando); depois, morte |
| Triceratops | só um acerto direto, e o primeiro derruba (Sangrando); depois, morte |
| Compsognathus | nunca mata: mordiscadas levam a Sangrando; com o jogador Caído, enxameiam |
| Estouro de Parasaurolophus, pés do saurópode | derruba (Sangrando) |
| Queda de 3 a 6 m | derruba (Sangrando); acima de 6 m (torres, falésias, sempre com guarda-corpo) é morte |

**A regra do primeiro contato** vale uma vez por espécie em cada partida, e o save a guarda. Ela existe para as espécies cuja cena de
apresentação uma rota pode pular (seção 5.8). Os raptors não precisam dela, porque as cenas deles são obrigatórias.

**Ferimento sem HUD:**

- **Saudável → Sangrando.** Dura 90 s, ou até uma bandagem. Gotas de sangue no chão, o cheiro x2 para o T. rex, compys atraídos de 30
  m. O jogador percebe pela respiração pesada e pela mão com sangue quando olha para baixo.
- **Sangrando → Caído**, se for atingido de novo por algo que fere. Caído no meio de compys, o jogador tem 5 s para se soltar
  chutando. Se não se soltar, os guinchos chamam o caçador mais próximo, e o bando o segura; sem caçador a 80 m, o bando se espalha
  depois de 10 s.
- **A bandagem** leva 4 s e só se faz num abrigo garantido. Ela limpa o Sangrando.

**Salvamentos** (`checkpoints` no JSON):

- **Automáticos e silenciosos** em cada subpasso de objetivo (cada disjuntor do O7, cada cartão, cada documento com código), em cada
  entrada numa zona, ao sair de um abrigo garantido e antes de cada cena com checkpoint (tabela 5.8).
- **Adiados até a hora segura:** só quando o diretor está Quieto e não há criatura em Investigate ou Chase a até 40 m. Se isso não
  acontecer em 120 s, fica valendo o anterior. Assim nenhum save cai no meio do perigo (o O2 ao lado do DL-03, o O7 com o bando B).
- **Manuais** nos terminais dos abrigos, como na seção 5.5, pela sensação.
- **Meta:** mediana de 8 minutos ou menos de jogo perdido por morte, e 12 minutos no pior caso. Os playtests da demo registram as
  mortes e os minutos refeitos.

### 6.10 Verbos do jogador

O `PlayerController` tem andar, agachar e correr, com cápsula agachada de 1,1 m; não tem pulo, escalada, deitar nem escada [M]. Os
contra-ataques das espécies dependiam de verbos que não existem. Esta é a lista, com o que cada um enfrenta [D]:

| Verbo | Especificação | Contra | Na demo? |
|---|---|---|---|
| Andar, agachar, correr | já existem; correr a 6 m/s, sem fôlego | todos | sim |
| Atirar | já existe; ruído de 24 m no ponto de queda | raptors, compys, Dilophosaurus | sim |
| Lanterna em Alta, Baixa, Apagada | seção 6.6 | todos | sim |
| Carregar (o galão, o módulo) | as duas mãos: sem atirar; lanterna presa no peito, só em Baixa; correr cai para 5 m/s; chacoalha (6 m andando, 12 m correndo); largar leva 0,5 s e faz 12 m | — | sim |
| Chutar | alcance de 1 m; espalha 1 ou 2 compys por 3 s; ruído de 6 m; solta o jogador Caído | compys | sim |
| Escada | subida exposta de 3 a 5 s; ruído de 8 m; sem lanterna enquanto sobe | — | sim (torre V1) |
| Sintonizar o receptor | 1 s por frequência; fone ou alto-falante (3 m) | — | sim |
| Bandagem | 4 s, num abrigo garantido | Sangrando | sim |
| Rastejar | cápsula de 0,6 m; 1 m/s; ruído de 1 m | passagens de 0,9 a 1,2 m: bueiros, túnel de tronco, fendas | não |
| Escalar | obstáculos de 0,6 a 1,4 m em 1 s | compys (subir em rochas e veículos) | não |
| Prender a respiração | dentro de um esconderijo, parado, até 6 s; depois, arquejo de 6 m | checagem de esconderijo | não (a chance é 0 no passo 1) |
| Comida | atirar ração; um bando come por 15 a 20 s | compys | não |
| Varredura da Yagi | 2 a 3 s; lanterna em Baixa; ruído de 3 m | — | não |

Os verbos da demo entram na F3; os outros na F4 (seção 8.2).

---

## 7. Técnica

### 7.1 O que foi medido e vale para a ilha inteira

| Fato | Valor | Registro |
|---|---|---|
| Núcleo jogável da floresta | 1,87 ha num tile de 256 m (6,55 ha) | spec 3.1, D2 |
| Instâncias e GameObjects | 5.825 e 23.274 por tile. Média do tile: ~3.550 GameObjects por ha. Cobrando o tile inteiro do núcleo: 12.446 por ha. Corrigido pelos ~70 % das árvores que ficam no núcleo: **~9.000 por ha** | D2 seções 1 e 6; `technical.md` 1 |
| Pior vista (player, 2560 x 1440, lanterna e vento ligados) | 10,42 a 10,60 ms (R1); 10,74 a 11,78 ms (R2); até 12,08 ms com empurradores. **Os valores de R2 são estimativas pareadas** (a leitura de 11,51 ms do N1 mais deltas medidos); a confirmação de 29 vistas numa sessão normal ainda está devida | STATE.md, W1 14.8 |
| Ruído do instrumento | o mesmo build varia 1 a 1,5 ms entre sessões; o delta pareado em pano_90 variou de -0,70 a +0,48 ms entre rodadas | D2 seção 2; W1 14.8 |
| Orçamento | 12,5 ms de mediana e 15 ms no p95 (janelas de 16 quadros) | D2 |
| Chão da cena vazia | uns 3,3 ms, então o conteúdo da floresta custa de 7,4 a 8,5 ms | D2 seção 7 |
| Lanterna | +0,5 a +1,3 ms; dois terços disso são sombras de folhagem com alpha test | N1 4.2 |
| Vento e curvatura | +0,56 a +1,23 ms | W1 12.3 |
| Empurrão da folhagem | faixa de 0,3 a 2,2 m de altura (`ForestWindInstances.BandTop`), no máximo 16 carimbos (`FoliageInteractionField.MaxStamps`), janela de 51,2 m em volta da câmera. "Três raptors" apertam 135 a 251 instâncias e custam +0,20 a +1,06 ms. Galhos cruzados ainda atravessam corpos | W1 13.8, 13.9, 14.8, 14.11; código |
| Caules finos | a regra do W1 desligou a cápsula do tronco de 690 dos 1.706 abetos jovens (40 %); o bake do NavMesh só junta colisores ligados | W1 seção 2 |
| O que paga a densidade | névoa de 30 m com corte a 50 a 55 m; sombras da lua de 30 m em 2 cascatas (economiza 1,25 a 1,7 ms) | D1, W1 |
| GPU occlusion culling | cortou 0 instâncias através das agulhas e custou 0,6 a 1,9 ms. Só reavaliar com oclusores sólidos | STATE D1 |
| Memória | texturas a 930 MiB (802 MiB no M3), malhas de 358 a 382 MiB | STATE verify-M4, M3 |
| Terreno | HDRP Terrain Lit: no máximo 8 camadas por tile | `terrain-lit-material.md` |
| Contrato congelado | `ForestClass`, `ForestSurface`, `ForestPrefabIds`; um tile; células de 0 a 7 | `ForestContracts.cs` |
| Disco por tile | `Terrain.asset` 46,9 MB, cena 20 MB, `placements.json` 3,9 MB, NavMesh 1,9 MB (medido só para o tipo M): uns 74 MB | `open-world.md` 2.3 |
| Medições que não existem | andar (quedas de LOD com cross-fade desligado); dinossauros entre 2.500 troncos | D2 seção 9; STATE linha 1190 |

**Consequência para o conjunto carregado.** A v1 orçava "9 a 12 células, ou 52 a 70 mil GameObjects". Esse número usou a média do
tile, que é quase toda fundo esparso. Na densidade que o design pede para B1 e B2, uma célula de 128 m (1,64 ha) tem de 15 a 20 mil
GameObjects, e 9 a 12 células dão **130 a 245 mil**. Só a zona da demo (8,03 ha de B1 e 1,80 ha de B2) teria **90 a 120 mil**, de
4 a 5 vezes a cena de hoje [E, aritmética sobre M]. Por isso os props pequenos sem um GameObject cada (SIS-5) entram antes da arte da
demo, e a auditoria de cada tile tem um teto: **6.000 GameObjects por célula de 128 m fora dos locais** [D]. Um tile que passe disso
não compila.

### 7.2 Streaming por tiles

O Unity 6000.6 não tem streaming de mundo nativo; mundos abertos são feitos de cenas aditivas ou Addressables (DOC
`Manual/WorldBuildingFeature.html`). Estrutura:

```
Island.unity (persistente, sempre carregada)
  Player, Game (sessão, objetivos, save, WorldState), Director, Population (estado abstrato de todas as criaturas)
  Lighting/ preset noturno global, lua, céu; Volumes de névoa e de sombra por zona (globais, misturados pela câmera)
  Navigation/ um NavMeshData por tipo de agente, para a ilha toda; os NavMeshLinks de todos os portões
  Backdrop/ silhuetas e luzes dos marcos (L1 a L10), malha de baixa resolução da ilha com a copa como casca escura
  WorldStreamer (F4)
Tiles/T_<i>_<j>.unity     tile de 256 m: Terrain + TerrainCollider, limites de zona
Cells/C_<x>_<z>.unity     célula de 128 m: as 4 x 4 células de 32 m da floresta, colisores, interativos
Sites/<site>.unity        locais planejados, carregados com a sua célula; cada interior com o seu Volume local
```

**A demo não faz streaming.** Se o SIS-5 deixar Z1 abaixo do teto de GameObjects e de memória medido na sonda SD2, os 5 tiles de Z1
carregam inteiros atrás de uma tela de carregamento. O `WorldStreamer` e as células entram na F4 (a parte F1b da seção 8.2). A demo
continua montada no layout final.

| Ajuste | Valor inicial | Por quê |
|---|---|---|
| Tile de terreno | 256 m, heightmap de 1025 (0,25 m), alphamap de 2048 nas rotas e de 1024 nos tiles de fundo | Os valores provados da floresta (spec 3.1, 6.4) |
| Raio de carga de tile | carrega a 200 m e descarrega a 260 m | No máximo 3 x 3 tiles |
| Célula de objetos | 128 m; carrega a 150 m e descarrega a 190 m | O corte de 55 m, mais um sprint de 6 m/s por uns 8 s de margem, mais histerese (sonda SD1) |
| Cargas simultâneas | 1, a mais próxima primeiro, puxando para a direção do movimento | Mantém a integração numa fatia de 4 ms |
| `backgroundLoadingPriority` | BelowNormal (4 ms) | Limita só a integração de texturas, malhas e áudio no main thread ([doc](https://docs.unity3d.com/ScriptReference/Application-backgroundLoadingPriority.html)) [V]. A ativação de objetos (Awake e OnEnable de ~20 mil componentes, o registro no GPU Resident Drawer, o registro do vento) **não** é documentada como fatiada: NÃO VERIFICADO, e o plano supõe que cai num quadro só (SD1) |
| Origem | O centro da ilha. A 2 km, float32 resolve 0,12 mm | Não precisa de origem flutuante |
| Campo de interação da folhagem | Uma janela de 51,2 m que segue o jogador; registros por célula (SIS-8) | Independe dos tiles |
| Shaders | Pré-aquecer os estados de pipeline com `GraphicsStateCollection`, gravada ao longo da rota da sonda SD11 ([doc](https://docs.unity3d.com/ScriptReference/Rendering.GraphicsStateCollection.html), "em geral substitui `ShaderVariantCollection`") [V] | A primeira compilação no Metal de cada material novo (criaturas, prédios, kits) é um tranco que a mediana de 16 quadros esconde |

Três limites do motor importam aqui: **texturas de terreno não fazem mipmap streaming** (cada tile carregado custa tudo), a oclusão
pré-calculada não serve para tiles, e o GPU Resident Drawer aceita qualquer MeshRenderer, mas não Skinned Mesh Renderers (seção 7.6).

### 7.3 Como o gerador da floresta escala

O gerador continua determinístico e feito por código. Cada passo deixa a cena da floresta compilável, como aconteceu nos marcos da
floresta (`open-world.md` 2.3).

1. **Um contrato novo em vez de editar o congelado.** `IslandContracts.cs` traz três coisas:
   - `IslandPlacement`: os campos da floresta, mais `tileX, tileZ`. **Só os objetos dos locais** (vindos dos dados da ilha) têm id
     salvo, um GUID; a dispersão nunca é salva (seção 7.7);
   - `IslandTile`;
   - `IslandPaths`.

   O código da floresta passa a receber um contexto de tile em vez de ler estáticos.
2. **Um asset de dados da ilha como fonte única** (`Assets/Game/World/IslandData/`). O `map-spec.json` v2 é o rascunho de entrada
   desse asset. O asset guarda:
   - a costa como campo de distância com sinal;
   - a receita de altura (anel, cume, caldeira, grotas, terraço);
   - o mapa de zonas, com um id por célula de 4 m, e o corredor jogável de Z3;
   - rotas e trilhas em espaço de mundo;
   - as áreas reservadas dos locais;
   - os territórios por passo, os abrigos, os portões e as cenas;
   - as paletas de terreno (`terrainLayers`).
3. **Passo da ilha (grosso, uma vez por build).** Campos a 1 m: altura macro, pesos de zona, distância a rotas, costa e locais, e
   inclinação. Uma grade de 1.800 x 1.500 dá 10,8 MB por campo. **Todos os objetos grandes** (árvores, rochas, troncos, tocos: os
   que têm colisor e definem os corredores) são postos nesse passo, então o espaçamento de Poisson nunca quebra numa emenda.
4. **Passo do tile (fino, por tile, em qualquer ordem).** Relevo fino, escavações e pintura a 0,25 m com uma borda de 16 m, e a
   dispersão das classes pequenas (samambaias, capim, galhos) pelo caminho do SIS-5. A semente vem de um hash de (semente da ilha,
   tile x, tile z, classe).
5. **Emendas exatas.** Os dois tiles avaliam a altura nas mesmas coordenadas de mundo na borda comum. Depois vêm `SetNeighbors` ou
   `allowAutoConnect` com um `groupingID`. A auditoria verifica que a diferença na emenda é zero.
6. **Mistura de zonas.** Os campos de densidade por classe viram tabelas por zona, misturadas pelo peso da zona. As camadas de cada
   tile são as 4 compartilhadas mais as próprias dos biomas que ele toca, com o recuo da seção 3.10.
7. **Carimbos por tile.** Um tile só é refeito quando muda o hash das entradas dele: os dados da ilha na área mais a borda, o hash do
   kit e a versão do código. Um manifesto de hashes por tile fica no git (seção 7.7).
8. **Auditoria e testes** (a lista está em `audits` no JSON). As regras atuais de assentamento, corredor, inclinação e spawn passam a
   valer por tile. Somam-se:
   - camadas distintas por tile ≤ 8;
   - GameObjects por célula de 128 m fora dos locais ≤ 6.000;
   - emendas iguais; nenhum objeto grande faltando ou duplicado numa emenda;
   - `NavMesh.CalculatePath` completo entre locais ligados, por tipo de agente;
   - nenhum caminho para dentro de abrigo, para nenhum tipo de agente (os dutos levam aos labs);
   - área de GG alcançável dentro dos refúgios de B1 e das ilhas de abetos da caldeira = 0; a fração do núcleo de B1 alcançável por G
     é informada;
   - **o grafo de travas da seção 5.3 satisfazível em todos os estados de alcance**;
   - **a auditoria das apresentações** (seção 5.8);
   - as regras LV-1, LV-2 e LV-11, e os espaçamentos de quebra de rastro e de poças de luz;
   - **as tabelas deste documento batem com o JSON** (áreas, passos, abrigos, locais). A v1 tinha três áreas de território erradas
     (DEIN-A 26,97 ha no JSON contra 16,1 ha no polígono; DEIN-A3 28,32 contra 24,9; DEIN-B 17,73 contra 13,9) e um território
     (T-REX-2) com atos fixos onde o texto dizia evento.
9. **Saídas por tile.** A cena do tile com seu `Terrain.asset`, as cenas das células, `placements.json`, o mapa de densidade e um
   relatório.

**Sistemas novos que valem para a ilha toda.** Os tamanhos têm âncora no ritmo medido da floresta: **P = 1 a 3 agentes-dia** (como
o D2 e o N1, cerca de 1 dia cada [M]); **M = 3 a 6** (como o W1, vento e empurrão, 4 dias em 5 rodadas [M]); **G = 6 a 15** (sem
âncora no projeto).

| # | Sistema | Por quê | Tamanho [E] | Quando |
|---|---|---|---|---|
| SIS-1a | Vários tiles, sem streaming | O builder faz um tile e uma cena | M a G (4 a 8) | F1a |
| SIS-1b | Streaming de tiles e células | A ilha inteira não cabe carregada | M (3 a 6) | F4 |
| SIS-2 | Kits de bioma guiados por dados | `ForestClass`, `ForestSurface` e `ForestPrefabIds` estão presos aos abetos | M (3 a 6) | F2 (provado no B4) |
| SIS-3 | Paletas v2 e B8 fora do terreno | Máximo de 8 camadas por tile | P (1 a 3); mais M se precisar do Shader Graph de terreno | F1a |
| SIS-4 | Vista distante: impostores | — | **adiado** para depois do lançamento: a névoa no chão vai a 60 m e os mirantes usam o fundo da demo | — |
| SIS-5 | Props pequenos sem um GameObject cada, **mantendo vento e empurrão** | Samambaias, galhos e capim são a maior parte dos GameObjects; o vento (W1) funciona por renderer | M a G (4 a 12) | **F1a** |
| SIS-6 | Névoa global, sombra da lua e exposição por zona, por Volumes misturados pela câmera | O Local Volumetric Fog não passa de 55 m (seção 2.4) | P (1 a 2) | F2 |
| SIS-7 | Ambiente e passos por superfície | Não existe áudio; o mapa de superfícies existe | dentro da linha de áudio | F3 |
| SIS-8 | Vento e empurrão com streaming | O registro do vento é uma varredura única da cena | P (1 a 3) | F1a |
| SIS-9 | Cobertura do chão em escala (tapete de samambaia, capim de duna, gramados) | A grama da floresta são só 120 a 205 touceiras | M (3 a 6) | F5; rota escolhida pela SD16 na F4 |
| SIS-10 | Luz de interiores | Sem APV, o interior pega a luz do céu e a névoa global | P (1 a 3) | F5; a SD12 decide na F2 |
| SIS-11 | Campo de empurrão grosso para corpos grandes | A janela de 51,2 m e a faixa de 2,2 m não servem ao T. rex | P a M (0 a 4), só se a SD13 pedir | F4 |

**SIS-5: a rota sai de uma sonda de 1 dia (SD15).** Duas opções:

- **(a) Mesh LOD em samambaias, galhos e capim.** Um renderer por instância em vez de uma raiz com filhos de LOD, então uns 4
  GameObjects viram 1. O manual diz que o Mesh LOD "não cria GameObjects nem componentes novos" e só faz cross-fade com o GPU Resident
  Drawer ([Mesh LOD](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html)) [V]. O custo: o material não muda por LOD, então
  o material só-LOD0 de motion vectors da samambaia (botão 1 do W1) valeria em todos os níveis. Medir isso.
- **(b) Um `BatchRendererGroup` próprio por célula**, alimentado pelo `placements.json`, com os colisores da célula mantidos como
  GameObjects. O GPU Resident Drawer é feito sobre o BRG (`gpu-resident-drawer.md`), então o Shader Graph de vento já compila as
  variantes de DOTS instancing. O slot de cada instância iria em `unity_RendererUserValue`, como o W1 11.2 já usa. NÃO VERIFICADO se um
  BRG próprio entrega esse valor do mesmo jeito: provar com um teste de captura como `Wind_InstanceTermsMatchTheVertexShader`.

**SIS-8: vento e empurrão com streaming.** Hoje `ForestWindInstances.Register()` roda `FindObjectsByType<MeshRenderer>` uma vez, quando
o campo liga, e descarta o registro anterior (`ForestWindInstances.cs` linhas 100 a 107). Um renderer que chega depois fica sem slot e
perde o estado de empurrão que o R2 afinou; registrar de novo a cada célula seria uma varredura completa e zeraria todos os slots.
`FoliageInteraction.Density` é um mapa estático de uma planta só, e o kernel de instâncias avalia todos os slots a cada quadro (16.099
hoje, umas 10 vezes isso num conjunto denso de 3 x 3 [E]). A correção, na F1a:

- slots alocados e liberados por célula, com o estado guardado por slot;
- um mapa de densidade da ilha toda a 0,5 m (3.500 x 2.570 células x 2 camadas x 1 byte, uns 18 MB), ou um por tile;
- o kernel despachado só sobre os slots a até 30 m (a distância do vento), por uma lista compactada.

**Renderers comprados não são recomendados.** O Nature Renderer 6 Pro ($70) e o GPU Instancer Pro ($128) desenhariam fora do Shader
Graph de vento e do campo de empurrão. Adotá-los significa portar o vento do W1 ou perdê-lo (NÃO VERIFICADO se é possível).

### 7.4 Orçamentos por zona e por tile

**O que é "ambiente".** Terreno, vegetação, prédios, lâmpadas (com volumétricos), volumes de névoa, efeitos transparentes na névoa (a
pluma L3, as fumarolas, o barco em chamas L7, o fogo do parque de combustível) e água. A v1 deixava tudo isso de fora.

**O teto do ambiente sai de medições na F0, não de um número fixo** (decisão 6). Dentro dos 12,5 ms:

| Uso | Reserva | De onde vem |
|---|---|---|
| Criaturas na GPU e no render thread (skinning, sombras, draws) | medida na sonda SD10, com o encontro pesado da seção 7.6 | F0 |
| Variação do streaming | ~0,5 ms; zero na demo, que não faz streaming | [E] |
| Reserva | ~1,0 ms | [E] |
| **Teto do ambiente** | **12,5 - SD10 - 0,5 - 1,0**. Com SD10 = 1,0 ms, dá os 10,0 ms da v1 | |

**A floresta densa passa de 10,0 ms em 0,4 a 1,8 ms, e as alavancas da v1 não bastam.** O maior par era o corte a 40 m (0,67 ms) mais
os motion vectors desligados no LOD1 dos abetos jovens (cerca de 0,45 ms): no máximo 1,1 ms, e cada um cabe dentro do ruído de 1 a
1,5 ms entre sessões. As alavancas agora são:

| Alavanca | Economia | Custo |
|---|---|---|
| **Escala de render** (sonda SD14): o HDRP 17.6 faz resolução dinâmica no Metal com TAA Upscale ou STP; o STP sem suporte de hardware pede escala fixa (`Dynamic-Resolution.md`) [V]. A floresta já usa TAA (D2 seção 2) | NÃO VERIFICADO: volumétricos, luz e pós-processamento escalam com os pixels; o vento, que pesa nos vértices, e as sombras não. Espera-se uma economia parcial [E] | Nitidez, medida em capturas a 0,85 e 0,75 |
| **Densidade D2 só nos bolsões de furtividade** (até 30 % da área de B1 [D]) | o custo de B1 cai fora dos bolsões; os GameObjects também | B1 mais leve no resto |
| **Aceitar B1 a 11,0 a 11,5 ms** com no máximo 1 predador materializado nas células mais densas | — | O diretor fica mais contido na mata densa |
| Corte noturno de 55 para 45 m / 40 m | 0,22 / 0,67 ms | Visível na linha de árvores distante (W1 I2b) |
| Motion vectors desligados no LOD1 dos abetos jovens | ~0,45 ms | Foi escolha do dono no I2b, revertida no R1 |

**Como medir.** Toda sessão de medição roda uma **vista de calibração** (a pano_90 da floresta de hoje) no começo e no fim, e cada zona
é julgada como delta pareado contra ela. Sem isso, nenhuma alavanca abaixo de 1,5 ms se distingue do ruído.

**Plataformas.** Todos os números são de um M4 Max a 2560 x 1440 nativos. Se a demo for para a Steam, a pesquisa de setembro de 2026
mostra Windows em 95,03 %, macOS em 1,92 % e 1920 x 1080 como tela principal de 47,91 % dos usuários
([Steam survey](https://store.steampowered.com/hwsurvey/)) [V, `scope.md` 13]. O HDRP no DX12 ou Vulkan é outro backend, e nada foi
medido nele. Então: o teto é **por plataforma**, e uma medição num PC Windows de gama média a 1080p (sonda SD19) entra na saída da demo
se ela for pública (decisão 3). Para o PS5, não prometer paridade: um preset desde o primeiro dia, com escala de render e resolução
dinâmica por hardware, texturas de 2K e densidade D2 só nos bolsões. Medir só quando houver um kit de desenvolvimento.

**Outros orçamentos:**

| Item | Orçamento | Base |
|---|---|---|
| Triângulos na pior vista | até 21,5 M (a floresta faz de 17 a 21,5 M) | N1 6, W1 12.3 |
| GameObjects | ≤ 6.000 por célula de 128 m fora dos locais; Z1 inteira medida na SD2 | [D] |
| Memória do ambiente | **até 4 GiB**: dois kits de bioma (~2,5 GiB com limites de streaming), 9 tiles de terreno (~0,36 GiB), células e NavMesh (abaixo de 0,3 GiB) | [E] `open-world.md` 2.4 |
| Terreno por tile carregado | ~40 MiB (dois controles de 2048², heightmap e base map); os de fundo, ~16 MiB | [E] |
| Kit de bioma | ~1,3 GiB no padrão da floresta; na fronteira, dois residentes. Texturas de 2K em tudo que não é visto a um braço de distância | [E] |
| Luzes | No máximo 2 lâmpadas com sombra à vista além da lanterna; o resto sem sombra (cabe nas 2 a 4 lâmpadas por local de objetivo) | [M] N1 |
| Criaturas | ≤ 16 animadas a até 60 m; manadas distantes atualizam mais devagar | [D] |
| Quadro em movimento (sonda SD11) | 0 quadros acima de 50 ms; no máximo 1 por minuto acima de 25 ms | [D] |
| Disco gerado | 2 a 3 GB para a ilha toda; fora do git, com o manifesto de hashes no git (decisão 11) | [E] |

**Custo estimado por zona** (de 3.2): Z1, Z2 e o corredor de Z3 são as mais caras (B1 e B2). Z5 e Z7 são abertas, mas com o chão a 60
m e a névoa acumulada da bacia. Z4 e Z6 são baratas na frente (rocha e paredes como oclusores) e caras nas luzes e nos efeitos
(vapor, Z6).

### 7.5 NavMesh

- **A armadilha.** NavMeshes separados que se sobrepõem não se ligam sem NavMesh Links (DOC `NavMeshLink.md`). Superfícies por tile
  exigiriam links em todas as emendas, para todos os tipos, no meio da vegetação. E criaturas em tiles descarregados ficariam sem
  NavMesh.
- **A solução: um `NavMeshData` por tipo de agente para a ilha toda**, montado no editor com `NavMeshBuilder.BuildNavMeshData`. As
  fontes são do tipo Terrain (o `TerrainData` de cada tile) e Capsule ou Box (os colisores primitivos dos planos de posicionamento). O
  gerador já conhece cada colisor, então o bake não precisa abrir as cenas. O NavMesh fica sempre carregado em `Island.unity`, e
  `UpdateNavMeshDataAsync` refaz só as partes que mudaram. Na demo, só Z1 e só os tipos P, M e G.
- **Cada tipo tem a sua lista de fontes.** Os caules finos (os 40 % de abetos jovens sem cápsula ligada) entram como obstáculo para G
  e GG e ficam de fora para M e P. O teste `Scene_PassableStemsFollowTheRule` (`NavMesh.Raycast`) roda uma vez por tipo.
- **Quatro tipos de agente, cada um assado só onde é preciso:**

| Tipo | Raio / altura | Voxel | Espécies | Onde assar | Regras de largura |
|---|---|---|---|---|---|
| P | 0,25 / 0,5 m | 0,10 m | Compsognathus | COMPY-1 a 4 e as bordas: Z1, Z2, Z6, sul de Z5 | apertos só por `NavMeshLink` gerado pelo gerador de locais |
| M | 0,5 / 1,8 m (o atual) | 0,167 m | Deinonychus | DEIN-A, A-VILA, A3, B, B3, mais Z1 | passa ≥ 1,5 m; barrado ≤ 0,9 m |
| G | 1,0 / 2,5 m | 0,25 m | Dilophosaurus | DILO-01 a 03 | caules finos contam |
| GG | 2,0 / 5 m | 0,33 m | T. rex, Triceratops | T-REX-1 a 3, TRI-1 e 2 | faixas ≥ 6 m livres, raio de curva ≥ 15 m; barrado < 4 m |

  O manual pede de 4 a 6 voxels por diâmetro de agente e diz que dobrar a resolução quadruplica o tempo de build
  ([voxelSize](https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings-voxelSize.html)) [V]: o P a 0,10 m custa várias vezes o M
  por área, por isso só é assado nos territórios dos compys. Parasaurolophus e Brachiosaurus andam por spline e não usam NavMesh.
- **Memória.** 1,9 MB por tile por tipo foi medido só para o M [M]. A sonda SD4 mede, por tipo, o tempo de bake, o tamanho por tile e o
  pico de memória do Editor, que precisa de todos os `TerrainData` carregados para assar. A estimativa da v1, 80 a 150 MB assando só
  nos territórios, fica [E] até lá. Se for muito, a saída é NavMesh regional a 600 m mais o grafo de territórios.
- **Portões, portas e cercas.** Na v1 eles usavam carving de `NavMeshObstacle` ou custo de área. Só que o carving só existe com a
  célula do obstáculo carregada, criaturas abstratas cruzam células descarregadas, e o custo de área é por tipo de área (32 no total),
  não por portão. Na v2:
  - toda abertura de jogo é assada **fechada**: portão, porta, brecha de cerca, os destroços do Passo Leste e as portas dos abrigos;
  - cada abertura é um `NavMeshLink` (componente ou `NavMesh.AddLink`) que pertence a `Island.unity` e que o `WorldState` liga e
    desliga, com qualquer célula carregada ou não;
  - o carving fica só para props de física perto do jogador.
- **Duas camadas de criatura** (a v1 tinha três):
  - **materializada** (até 150 m, ou visível de um mirante, ou numa cena): IA completa, `NavMeshAgent` e animação. A visão precisa
    dos colisores das células, porque o `RaptorAI` usa linecasts de física;
  - **abstrata** (o resto): um nó num grafo de waypoints pré-calculado sobre trilhas e territórios, que anda a 1 Hz e ouve os ruídos
    globais. O NavMesh só entra em trechos de 150 a 200 m ou menos, porque `CalculatePath` é síncrono e caminhos longos podem voltar
    parciais (relato de fórum da 5.4,
    [Unity Discussions](https://discussions.unity.com/t/is-there-a-cap-on-navmesh-calculatepath/177160); NÃO VERIFICADO na 6000.6).
  - **A promoção** é por distância **e** por linha de visão de um mirante, e as cenas encenadas forçam a materialização. Os herbívoros
    de spline são sempre desenhados até a visibilidade da zona, com LOD baixo e o Animator em ritmo reduzido, porque não têm IA para
    pagar.

### 7.6 Criaturas: importação e orçamento

**O que tem risco baixo.** Malhas e materiais: a Pxltiger entrega mapas Standard, e o HDRP Lit precisa de um mask map (R metálico, G
AO, B detalhe, A suavidade), montado por script de editor como o `ForestMaterials.cs` faz. Os LODs vêm nos pacotes e vão num LOD
Group; o Mesh LOD automático do Unity 6.6 "não reduz o trabalho de deformação" em Skinned Mesh Renderers
([manual](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html)) [V]. A escala é conferida contra o comprimento real na
importação: Deinonychus 3,4 m, Dilophosaurus 6 a 7 m, T. rex 12 m. O material do olho tem emissão pelo ângulo e pelo estado da
lanterna, que dá o brilho dos olhos (seção 6.6).

**O que tem risco: a animação.**

1. **Pé no chão.** Os rigs são Generic, e o IK nativo do Unity é só para Humanoid
   ([Animancer](https://kybernetik.com.au/animancer-v7-4/docs/examples/jobs/two-bone-ik)) [V]. O pacote Animation Rigging não está no
   `manifest.json`, e o Two Bone IK dele tem um bug conhecido de deslocamento em rigs Generic com avatar "Create From This Model"
   ([issue tracker](https://issuetracker.unity.com/issues/9969/animation-rigging-two-bone-ik-gets-offset-when-a-generic-rig-avatar-is-created-from-an-existing-model))
   [V]. O núcleo da floresta chega a 25,8 graus de inclinação [M]: sem IK de pé e inclinação do corpo, os pés de animais de 2 a 9 t
   flutuam ou afundam no facho. Rota: Animation Rigging com "No Avatar", ou jobs de IK próprios.
2. **Velocidade.** As velocidades do design (raptor 9 m/s, T. rex 8 m/s, Dilophosaurus 6,5 e 10 m/s) podem não bater com a velocidade de
   raiz dos clipes, e os pés escorregam. Um script extrai uma tabela de velocidade de raiz de cada clipe de locomoção, e a velocidade de
   jogo se ajusta a ela ou o clipe é reescalado.
3. **Câmera de morte.** Nenhum pacote tem captura ou morte sincronizada com uma câmera em primeira pessoa. Cada predador monta a sua com
   clipes de mordida e um script de câmera.
4. **Clipes que podem faltar:** farejar e rastrear (T. rex, raptors, a checagem de esconderijo), a exibição da crista do Dilophosaurus
   (só há rugidos e 9 poses aditivas de cabeça), a mordiscada, o acosso e a dispersão dos compys (a lista da DANKIE não é publicada,
   NÃO VERIFICADO). Rigs Generic não dividem clipes entre espécies e não há animador na equipe, então um clipe que falta sai caro.
5. **Penas.** A construção (cascas ou cartões com alpha) não é declarada e soma custo no mapa de sombra da lanterna. Medir na SD10.

**Antes de comprar o resto: a matriz de cobertura e a sonda de importação (SD17).**

- **Matriz:** cada estado de IA da seção 6, vezes cada espécie, vezes o nome do clipe. Feita na F0 para as 7 espécies, com as listas
  publicadas.
- **Sonda SD17, na F0, com os três corpos da demo** (Deinonychus, compy e Dilophosaurus, $94,99), de 2 a 3 agentes-dia: o script de
  material, a tabela de velocidade de raiz, IK de pé e inclinação do corpo numa rampa de 25 graus, um protótipo de câmera de morte, e a
  SD10 com o Deinonychus no `RaptorAI` atual dentro da floresta.
- **Orçamento: 4 a 8 agentes-dia por espécie com IA completa**, não 2 a 5 [E, `technical.md` 9]. As partes comuns (IK, velocidade de
  raiz, câmera de morte, olho) são feitas uma vez, na F3.

**Corpos grandes na folhagem.** O empurrão foi afinado com o jogador e com corpos do tamanho de um raptor (seção 7.1). Com os animais da
ilha:

- o tronco de um T. rex, a 2 a 4 m de altura, atravessaria as copas dos abetos jovens (a faixa acaba em 2,2 m);
- jogador, 3 raptors, 6 Parasaurolophus e um bando de 10 compys seriam 20 carimbos, contra 16;
- nada se curva além de 25,6 m da câmera, então um T. rex passando pela samambaia a 40 m a deixaria parada;
- o custo das instâncias apertadas por corpos GG não foi medido.

A correção:

- **sonda SD13 de corpo grande** (F4): empurrador de 2 m de raio e 12 m de comprimento, faixa até 4,5 m, por uma borda de moita e pela
  cova das samambaias; mede instâncias apertadas e ms no harness pareado;
- **carimbos por prioridade:** o jogador e os corpos grandes mais próximos primeiro; um bando de compys divide 1 ou 2 carimbos;
- **um segundo campo grosso** (128 x 128 texels de 1 m) só para corpos grandes nas zonas abertas, se a SD13 mostrar que precisa
  (SIS-11);
- **direção própria para GG**, com raio mínimo de curva, e o T. rex em B1 só por faixas de 6 m ou mais (seção 4.8);
- o `FoliagePusher` procura um `CharacterController` nos pais (`FoliagePusher.cs` linha 92), e as criaturas usam `NavMeshAgent`, então
  elas entram pelo caminho `Configure()` que o proxy do harness usa.

**Orçamento.**

- Um encontro pesado (T. rex, 3 raptors, 6 compys e 6 Parasaurolophus) soma uns **138 mil triângulos no LOD0**, 1,3 % da floresta [E].
- **Draws.** O GPU Resident Drawer só aceita Mesh Renderers (`gpu-resident-drawer.md`) [V], então as criaturas desenham do jeito
  clássico: cada submalha em cada passe (profundidade, GBuffer, motion vectors, 2 cascatas da lua, a lanterna e as lâmpadas). 20
  criaturas com umas 4 submalhas dão de 500 a 600 draws, tantos quanto todos os comandos do drawer da floresta [E]. É custo de CPU no
  render thread, que a contagem de triângulos não mostra. **A SD10 mede draws, SetPass e o tempo do render thread.**
- Texturas de **2048 por padrão** (~160 MB para 7 espécies, contra ~630 MB em 4K). Só o T. rex e o raptor mantêm albedo e normal em
  4096, porque são vistos de perto.
- Animator em `CullUpdateTransforms`.

**Áudio.** Nenhum pacote traz vocalizações (seção 8.4).

### 7.7 Salvamento

| Pergunta | Resposta |
|---|---|
| O que se salva | Jogador (posição, estado de ferimento, inventário), objetivos e subpassos, o `WorldState` (energia por setor, portas, portões, cercas, cartões, cenas vistas, a regra do primeiro contato por espécie), coletáveis pegos, documentos lidos, relógio e passo, estado do diretor, e a camada, posição, território e desbloqueios de cada criatura |
| O que não se salva | O cenário gerado, que é refeito igual a partir da semente. Objetos atirados voltam ao lugar |
| IDs | **Só objetos dos locais**, com um GUID nos dados da ilha. Ids de índice do gerador (tile, célula, índice) quebrariam a cada regeneração, e haverá regenerações toda semana da F2 à F6 e nos patches. A dispersão nunca é salva. Nunca instance IDs |
| Estado vivo | Um registro `WorldState` em `Island.unity`. Todo prefab com estado o consulta no Awake, e ele também liga e desliga os `NavMeshLink` dos portões (seção 7.5) |
| Formato | JSON com `JsonUtility` em `Application.persistentDataPath`, com campo `version` e uma migração por versão. Algumas dezenas de KB |
| Quando | Automático e silencioso nos subpassos, nas entradas de zona, ao sair de abrigo e antes das cenas, **adiado até o diretor ficar Quieto e sem criatura em Investigate ou Chase a até 40 m** (seção 6.9). Manual nos terminais dos abrigos, recusado só durante um Chase a até 40 m |
| Carregar | Carrega `Island.unity`, aplica o save e carrega as células em volta antes de devolver o controle (com fade). O diretor começa em Alívio |
| Determinismo | Provado numa máquina só: `placements.json` idêntico byte a byte [M]. Entre máquinas e versões do Unity, NÃO VERIFICADO. Por isso um manifesto de hashes por tile fica no git, e o que é publicado sai só da saída da máquina de build |

### 7.8 Sondas (no player de desenvolvimento do macOS, sempre pareadas contra a vista de calibração)

As sondas se chamam SD1 a SD19 para não se confundir com os locais (S01 a S29); as da v1 mantêm o número (a S1 é a SD1). A E2 é o
teste de plantas, que vem da série de experimentos da floresta.

| # | Pergunta | Como | Passa se | Fase |
|---|---|---|---|---|
| E2 | As plantas modeladas passam ao lado dos scans? | Comprar Dicksonia, Equisetis, uma araucária e um bracken (~$40). Passar pelo pipeline e capturar ao lado de um abeto jovem a 1,5 e 6 m, sob a lanterna | Aprovação do dono na captura (decisão 2) | F0 |
| SD10 | Quanto custam as criaturas? | O Deinonychus no `RaptorAI` atual dentro da floresta, mais uma vista de "estresse de criaturas" com os três corpos da demo repetidos até o encontro pesado da 7.6 | Mede a GPU, os draws, o SetPass e o render thread; o resultado fixa o teto do ambiente | F0 |
| SD14 | Quanto rende a escala de render? | TAA Upscale e STP a 0,85 e 0,75 contra o TAA nativo de hoje, 29 vistas, pareado | Mede ms e nitidez; o dono aprova as capturas | F0 |
| SD15 | Qual rota do SIS-5? | Mesh LOD contra BRG próprio, numa célula densa | GameObjects por célula, ms, e o teste de captura do vento | F0 |
| SD17 | A animação dos corpos comprados serve? | Material, velocidade de raiz, IK numa rampa de 25 graus, câmera de morte, com os três corpos da demo | Pés no chão no facho; nenhum clipe essencial faltando | F0 |
| SD1 | Quanto custa carregar? | Uma cena de tile completa (46,9 MB de `TerrainData`, colisor de 1025², dois alphamaps de 2048²) e uma célula densa de 128 m, cronometrando o quadro de ativação à parte | Nenhum quadro acima de 50 ms; célula visível em até 3 s | F1a |
| SD2 | Quanto custa o conjunto carregado? | Z1 inteira e um 3 x 3 de células densas cortadas do núcleo (~20 mil GameObjects cada antes do SIS-5) contra 1 x 1, na mesma vista | Dentro do teto de GameObjects; no máximo 0,5 ms pior na pior vista. **Portão duro da demo** | F1a |
| SD3 | As emendas batem? | Dois tiles vizinhos gerados com campos de mundo | Diferença 0 e nenhuma rachadura nas capturas | F1a |
| SD8 | A transição de névoa entre zonas segura? | Mata (30 m) para planalto (60 m) e de volta, com Volumes globais misturados pela câmera | Exposição estável, sem cintilação, nos dois sentidos | F2 |
| SD9 | O que se lê na névoa, e a que custo? | Cartões emissivos a 50, 100, 200 e 300 m; Lens Flare (SRP) com e sem oclusão; pisca vermelho atrás da copa; câmera a Y = 0, 30, 45 e 60 m; uma poça de luz de local de objetivo vista de 25 a 35 m | Aprova ou corrige as regras da 2.4 e a LV-3 | F2 |
| SD12 | Quanto custa um interior? | A Quonset da demo: agentes-dia gastos, custo de quadro, visual, e o Volume local de interior | Recalcula os outros 5 interiores herói; decide se precisa de APV | F2 |
| SD11 | Como é o quadro andando? | Uma rota roteirizada pela demo, andando e correndo a 6 m/s, com IA e áudio ligados e vsync a 60 Hz; grava a `GraphicsStateCollection` | p99; quadros acima de 16,7, 25 e 50 ms. Passa com 0 acima de 50 ms e no máximo 1 por minuto acima de 25 ms | F3 |
| SD19 | E num PC Windows? | Um PC de gama média a 1920 x 1080, DX12 | Dentro do teto por plataforma (só se a demo for pública) | F3 |
| SD4 | O NavMesh da ilha sai das fontes? | 2 x 2 tiles, 4 tipos, `BuildNavMeshData` | Por tipo: tempo, tamanho por tile e pico de memória do Editor; `CalculatePath` completo atravessando emendas | F4 |
| SD5 | Quanta memória? | Contadores com 9 tiles e 12 células | Ambiente até 4 GiB | F4 |
| SD6 | O save sobrevive ao streaming? | Mudar interruptores, salvar, sair, carregar em outro lugar | Estado idêntico e nenhum id órfão | F4 |
| SD7 | Quanto CPU custa a IA fora de cena? | 20 criaturas abstratas e 3 materializadas | Main thread até 4 ms | F4 |
| SD13 | Corpo grande na folhagem | Seção 7.6 | Mede instâncias e ms; decide o SIS-11 | F4 |
| SD16 | Qual rota para a cobertura do chão? | Detalhes de terreno instanciados com o material de vento (vêm no `TerrainData`, sem GameObjects; o vento funciona sem slot; o empurrão é NÃO VERIFICADO; sem LODs) contra o BRG do SIS-5; mais uma fatia de B3 a 60 m | Ms, memória e captura | F4 |

---

## 8. Produção

### 8.1 A demo (20 a 30 minutos)

**O que ela é.** É uma fatia da ilha final, não uma fase separada: a zona Z1 já com o layout da ilha inteira, onde só os 5 tiles de
terra de Z1 têm arte final. As bordas são travas reais: o Portão da Mata e a Cerca da Grota, que o jogo completo abre, e a Ponte com
corrente, caída de vez. Atrás delas, na demo, há paredes invisíveis, como as 24 caixas de limite da floresta hoje (spec 3.6). Z1
carrega inteira, sem streaming. É também a **corrida de calibração**: cada item registra os agentes-dia reais, e esses números
replanejam o resto (portão G-replan, seção 8.2).

**Conteúdo.**

- **Zona:** Z1, com 15,3 ha.
- **Biomas:**
  - B1 (já existe);
  - B4, o paliteiro, que é quase de graça e força o SIS-2;
  - B2, a grota baixa, com o kit da E2. Se a E2 falhar, a grota vira uma ravina de B1 com samambaia densa e abetos jovens, que é o
    habitat do Dilophosaurus "como já construído";
  - B7, só o alto da falésia da enseada, com faces de rocha enterradas e o plano de mar mínimo;
  - B8: a Estação de Campo, o galpão e o portão.
- **Objetivos:** O1 a O3 e um abrigo (SR1).
- **Espécies:** compys (sozinho e em bando), o Dilophosaurus DL-03 como clímax, a manada PARA-2 **só ouvida** (os dois chamados), os
  raptors no gancho final (o corpo Deinonychus num caminho roteirizado) e um rugido do T. rex na torre.
- **Sistemas:** diretor no passo 1, lanterna em três estados, volumes de ocultação, receptor com antena de chicote, morte e
  ferimento, checkpoints, terminal de save, os verbos da demo (seção 6.10) e de 8 a 12 documentos em texto.
- **Sem:** streaming, mar fora da enseada, cavernas e vista distante (SIS-4). A vista da torre usa emissivos, lens flare e silhuetas de
  baixa resolução, com um Volume de câmera que abre a névoa global no alto da torre.

**Roteiro**, com as posições da ilha e, entre parênteses, as posições locais da floresta. O pico fica na volta com o galão, e não na
primeira vista do Dilophosaurus, para que a demo não desça para os compys nos últimos 8 minutos.

| Min | Batida | Onde | Construir |
|---|---|---|---|
| 0 a 3 | O pescador acorda na saliência da falésia, com o barco em chamas nas pedras abaixo. Sobe a T1. Ao longe, os três chamados curtos da manada ("desconhecido") | início (-652, -280) → SouthBank (-645, -200) | Fogo e luz, plano de mar da enseada, arrebentação, trilha da falésia |
| 3 a 8 | Trilha da Mata para o norte. Primeiro compy, sozinho e curioso, atraído pelo ruído, inofensivo; o jogador aprende a atirar (cena CE1) | T2; Deadfall (-628, -142) | IA do compy |
| 8 a 12 | Na cova das samambaias, o tratador morto com o receptor e a ficha do DL-03. A Estação de Campo está escura e o gerador seco (**O1**); quadro de mapa e mapa de papel. Checkpoint | FernHollow (-606, -152) → S02 (-638, -57) | Receptor, 3 documentos, a Estação |
| 12 a 20 | Estrada da Mata pelo paliteiro, ao som da manada do outro lado da clareira. Descida para a grota (checkpoint na borda); o receptor bipa o ritmo de morte; camas de samambaia amassadas. No galpão, **a coleira arrancada**, e o DL-03 se mostra: exibição sob a lanterna, depois some e se reposiciona (cena CE2). O galão (**O2**) | R2 → S03 (-505, -255) | B4, B2 baixa, IA do Dilophosaurus |
| 20 a 26 | **O clímax: a volta com o galão** (cena CE3, 239 m em linha reta). O galão chacoalha, a lanterna fica presa no peito em Baixa, não dá para atirar, e correr cai para 5 m/s. O DL-03 espreita a volta pelos pontos de emboscada; o aviso tem resposta, e o primeiro bote derruba (Sangrando) em vez de matar. Na beira da falésia (T3), o bando 1 come uma carcaça, com um objeto para jogar a menos de 5 m (cena CE4) | T3 → S02 | Pontos de emboscada, verbo de carregar |
| 26 a 29 | Queda da tensão: o gerador (40 m de ruído) atrai o bando (CE5); o jogador se esconde no vão da Quonset ou chuta. A Estação vira o abrigo 1: salvar e bandagem. Rádio: estática com legendas, "...mastro principal...", e as três linhas no mapa. A chave do portão. A torre de 15 m (escada): **primeira vista da ilha**, com L1 vermelho, L2 farol, L3 pluma e L10 vila. Um rugido do T. rex, e aves levantando sobre a caldeira (**O3**) | S02, V1 | Torre, Volume de câmera, luzes e silhuetas distantes |
| 29 a 30 | **O gancho** (CE6): o chamado longo de "predador" da manada; pela janela, dois raptors cruzam o holofote do pátio, com brilho dos olhos a 1 m de altura. Não há perseguição. Fim | S02 | Corpo do Deinonychus num caminho roteirizado |

**A construir para a demo.**

- **Prédios por código:** 1 Quonset (6,1 x 14,6 m, o primeiro interior: sonda SD12), 2 contêineres, gerador, mastro de 18 m, torre de
  15 m, uns 140 m de cerca, o galpão e o portão.
- **O casco do barco:** um modelo CC0 ou um casco simples gerado por código (NÃO VERIFICADO se o Poly Haven tem um).
- **O mar da enseada:** um plano opaco com normais em movimento (0,5 a 1 agente-dia [E]).
- **IAs:** compy (sozinho e em bando), Dilophosaurus (emboscada, exibição respondível, bote, reposição), o corpo do raptor num caminho.
- **Sistemas:** diretor v1, lanterna, ocultação, receptor, objetivos, morte e ferimento, checkpoints, save, áudio ambiente e de
  criaturas, verbos, UI mínima (legendas, leitor de documento, mapa de papel, bússola, terminal), menu de depuração e registrador de
  partidas.

**Saída da demo** (`open-world.md` P2, mais as revisões):

- mediana de 20 a 30 min com 3 a 5 jogadores de primeira vez;
- pelo menos 2 picos do diretor por partida;
- 0 travamentos de progresso;
- mortes por partida e minutos refeitos: mediana de 8 minutos ou menos perdidos por morte, e 12 no pior caso;
- quadro: ambiente dentro do teto fixado na F0 e quadro até 12,5 ms de mediana (15 ms no p95) na pior vista com criaturas;
- andando (sonda SD11): 0 quadros acima de 50 ms e no máximo 1 por minuto acima de 25 ms;
- se a demo for pública: uma build Windows medida num PC de gama média a 1080p (sonda SD19);
- teste cego de áudio: espécie e estado acertados em 80 % das vezes ou mais;
- capturas aprovadas pelo dono;
- os agentes-dia reais de cada item, registrados para o G-replan.

### 8.2 Fases e esforço

A v1 dava de 76 a 130 agentes-dia para o jogo todo, mas as fases contradiziam as próprias taxas: a F1 (5 a 8 dias) continha o SIS-1,
que a 7.3 classificava como "várias semanas"; a F3 (8 a 14) continha três IAs novas a 2 a 5 dias cada; a F5 tinha 25 locais a 1 a 3
dias cada. Faltavam áudio, playtests, ferramentas, lançamento e as horas do dono [`scope.md` 1]. A tabela abaixo é de baixo para
cima: quantidade vezes taxa, com a origem de cada taxa, e já com os cortes e os acréscimos das três revisões.

**Taxas usadas** [E]: P = 1 a 3, M = 3 a 6, G = 6 a 15 agentes-dia (âncoras na seção 7.3); kit de bioma 1,5 a 2 (medido de M0 a M4,
`open-world.md` 1.4); espécie com IA completa 4 a 8 (`technical.md` 9); herbívoro roteirizado 1 a 3; interior herói 3 a 5, a
recalcular pela SD12; local exterior com até uma sala 0,5 a 1,5; zona (relevo, dispersão, névoa, marcos e 2 a 3 rodadas de captura,
como as 7 rodadas da floresta em 5 dias [M]) 1,5 a 3.

**A demo (F0 a F3)**

| Fase | Item | Agentes-dia [E] |
|---|---|---|
| **F0 Decisões e sondas de compra** | Decisões 1 a 6 e buscas de marca (0,5 a 1); E2 (1 a 2); SD17 e SD10 com os três corpos (2 a 3); SD14 (0,5 a 1); SD15 (0,5 a 1) | **4,5 a 8** |
| **F1a Espinha da demo** | SIS-1a (4 a 8); SIS-5 (4 a 12); SIS-3 (1 a 3); SIS-8 (1 a 3); NavMesh de Z1 para P, M e G, com fontes próprias, voxels, links e auditorias (1 a 3); menu de depuração (1 a 2); SD1, SD2 e SD3 (1 a 2) | **13 a 33** |
| **F2 Arte de Z1** | Kit B4 (1 a 2) e SIS-2 (3 a 6); kit B2 com a E2 (1,5 a 2); enseada: falésia, casco e plano de mar (1,5 a 3); a Quonset, interior herói e sonda SD12 (2 a 4); galpão e portão (1 a 2); SIS-6 (1 a 2); fundo da torre, SD8 e SD9 (1 a 2); Z1: relevo, dispersão e 2 a 3 rodadas de captura (2 a 3) | **14 a 26** |
| **F3 Jogo da demo** | Tecnologia comum de criatura: IK, velocidade de raiz, câmera de morte, olho (2 a 4); compy (3 a 6); Dilophosaurus (3 a 6); corpo do raptor e gancho (1 a 2); diretor v1 com papéis e vagas (2 a 4); lanterna, visibilidade do facho e ocultação (2 a 4); morte, ferimento, checkpoints, save e `WorldState` (3 a 5); receptor e coleiras (1 a 2); verbos da demo (1 a 3); O1 a O3, cenas CE1 a CE6 e documentos (2 a 3); UI mínima (1 a 3); registrador de partidas (1 a 2); áudio (3 a 5); SD11 e pré-aquecimento de shaders (1 a 2); 2 rodadas de playtest e ajuste (3 a 5); build Windows e SD19, se pública (1 a 2) | **30 a 58** |
| | **Total da demo** | **62 a 125**, ou **3 a 7 meses** |

**O jogo completo depois da demo (F4 a F6)**

| Fase | Item | Agentes-dia [E] |
|---|---|---|
| **F4 A ilha inteira em greybox, com todo o comportamento** | F1b: SIS-1b (3 a 6), duas camadas de criatura e grafo de waypoints (2 a 4), NavMesh da ilha por tipo e links dos portões (2 a 4), SD4 a SD7 (2 a 3); greybox das 6 zonas com kits provisórios e locais como caixas, auditorias de alcance e de travas (4 a 8); O4 a O13, passos, relógio e portões de mão única (3 a 5); diretor v2 (1 a 3); checagem de esconderijo e respiração (1 a 2); verbos restantes (2 a 3); bando de raptors completo (4 a 6); T. rex (5 a 8); Triceratops, Parasaurolophus e Brachiosaurus roteirizados (5 a 8); cenas CE7 a CE15 (3 a 5); vinhetas de rota (2 a 4); save completo (1 a 2); SD13 e carimbos por prioridade, mais o SIS-11 se preciso (1 a 4); SD16 (1 a 2); 2 a 3 rodadas de playtest e ajuste (5 a 8) | **47 a 85** |
| **F5 Arte por zona** | Kits B3, B5, B6 e B7 (6 a 8) e o chão de B8 como malhas e decals (1 a 2); SIS-9 (3 a 6); SIS-10 (1 a 3); 5 interiores herói (15 a 25); a vila (2 a 4); 12 locais com até uma sala (6 a 18); 6 zonas, Z2 a Z7 (9 a 18); arte das 4 criaturas compradas na F4 e F5 (2 a 4); o resto do áudio (5 a 10); 30 a 50 documentos bilíngues (2 a 3) | **52 a 101** |
| **F6 Polimento e lançamento** | Desempenho com a confirmação de 29 vistas por zona, saves entre versões, menus, acessibilidade, mixagem e a rodada final de teste (10 a 20); página da loja, builds e captura de trailer, se for Steam (3 a 6) | **13 a 26** |
| | **Total do jogo, com a demo** | **174 a 337**, ou **9 a 19 meses** |

**Comparação.** A revisão de escopo recontou a v1 em 140 a 330 agentes-dia, e 110 a 260 depois dos cortes; a técnica, em 100 a 170.
Esta conta fica acima das duas porque soma o que as três pediram juntas: o SIS-5 e o SIS-8 antes da demo, a morte e o ferimento, a
checagem de esconderijo, os avisos, os verbos, as cenas, as vinhetas e 4 a 8 dias por predador. Os cortes (SIS-4, U1, cinco locais
opcionais, a vila de 24 casas, o resto de Z3, a R8b, as ovelhas, a camada simulada, metade dos documentos, a tempestade, o shader de
placa e o eco) já estão fora.

**Calendário.** Mês = 26 agentes-dia (6 por semana, como na floresta) vezes 1,3 a 1,5 de espera pelas revisões do dono. Agentes em
paralelo só encurtam o calendário onde não há revisão do dono, e a revisão do dono é o gargalo.

**Horas do dono** [E]: na demo, de 19 a 34 h (decisões e capturas da F0, 6 a 10; capturas de Z1, 3 a 6; gravações e ajuste dos
playtests, 6 a 10; sensação das duas IAs, 2 a 4; direção de áudio, 2 a 4). No jogo completo, mais 32 a 81 h (6 zonas com 2 a 3
rodadas de 1 a 2 h; 4 espécies; 5 interiores; as rodadas de playtest da F4; áudio; lançamento). No total, **cerca de 50 a 115 h**. Com
as horas por semana do dono (decisão 3), isso vira semanas reais.

**Portão G-replan (depois da F3, antes de qualquer compra do jogo completo).** Comparar os agentes-dia registrados de cada local,
espécie e sistema da demo com as taxas acima. Refazer a F4 a F6 com as taxas medidas. Só então comprar o T. rex, o Triceratops, o
Parasaurolophus, o saurópode, os kits e os props. Se a conta nova passar do que o dono aceita, as alavancas são, nesta ordem: cortar
dois dos cinco interiores herói restantes (6 a 10 agentes-dia); fazer da bacia da caldeira só uma travessia opcional (Z7 sem a S14);
juntar as linhas B e C numa só (menos S19, S22 e O10 a O11).

### 8.3 A ordem das regiões depois da demo, e por quê

1. **A F4 (greybox da ilha, com todo o comportamento) vem antes de qualquer arte.** O ritmo, o grafo de travas, os passos do diretor e
   a auditoria das apresentações são provados onde mudar é barato. As espécies correm em corpos comprados com materiais provisórios,
   ou em cápsulas. É a regra da própria floresta: todo recurso arriscado se apoia num padrão que já funciona.
2. **Z2, Vila e Porto.** Reaproveita o kit de abetos e amadurece o pipeline de prédios por código em terreno fácil. Também decide o
   mar do porto (o plano da demo melhorado ou o HDRP Water System, medidos).
3. **Z3, o corredor das grotas.** Reaproveita o kit de B2 da demo e traz dois Dilophosaurus.
4. **Z4, Cumeada e Usina.** B5 (faces de rocha, fumarolas), a usina e o bando B. A virada do O7 é testada com arte.
5. **Z6, Complexo de Pesquisa.** O interior herói dos labs testa a luz de interior (SIS-10) com paredes sólidas como oclusores (D1
   nota 4).
6. **Z5, Planalto e Pista.** Samambaia alta (SIS-9), charneca `namaqualand`, manadas e a pista. Com o chão a 60 m ela deixou de ser a
   zona de maior risco de quadro, mas é a maior em cobertura do chão.
7. **Z7, Caldeira e Cume.** Usa B3 e B5 das zonas anteriores. A névoa acumulada da bacia barateia o fundo; o cume depende de todo o
   resto.

### 8.4 Lista de compras

Preços de tabela em USD, lidos em 2026-10-21 e 2026-10-22, sem impostos. A Asset Store tem promoções de 50 %, e esperar por uma pode
reduzir a conta (NÃO VERIFICADO). Cada kit e cada criatura do jogo completo só é comprado na fase da sua zona, depois do G-replan.

| Quando | Item | Loja | Preço |
|---|---|---|---|
| **F0** | Pxltiger Deinonychus (o corpo do `RaptorAI`) | [Asset Store 182587](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/deinonychus-182587) | $35,00 |
| F0 | DANKIE Compsognathus | [Asset Store 135639](https://assetstore.unity.com/packages/3d/characters/animals/prehistoric-dinosaur-compsognathus-135639) | $9,99 |
| F0 | Pxltiger Dilophosaurus | [Asset Store 286641](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dilophosaurus-wetherilli-286641) | $50,00 |
| F0 | Teste E2: BotaniCraft3D Dicksonia ($8,25) e Equisetis ($7,50), uma araucária e um bracken (preços não lidos) | [RenderHub](https://www.renderhub.com/botanicraft3d/dicksonia-tree-plant) | ~$30 a $40 [E] |
| F3 | Áudio de ambiente e foley: Sonniss GDC Game Audio Bundles. Sem royalties, uso comercial, sem atribuição, projetos ilimitados; proibido para treinar IA | [Sonniss](https://sonniss.com/gameaudiogdc) | $0 [V] |
| F3 | Vocalizações como matéria-prima: Magic Sound Effects "Dinosaur Vocalizations", 228 efeitos. A página diz ao mesmo tempo "sem limite de projetos" e "um projeto comercial": confirmar com o vendedor | [itch.io](https://magicsoundeffects.itch.io/dinosaur-vocalizations) | a partir de $15 [V] |
| F3, se pública | Steam Direct, por jogo; recuperado depois de $1.000 de receita | [Steamworks](https://partner.steamgames.com/doc/gettingstarted/appfee) | $100 [V] |
| | **Subtotal da demo** | | **~$140 a $285** |
| F4 | Pxltiger Parasaurolophus | [Asset Store 223710](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/parasaurolophus-walkeri-cyrtocristatus-dinosaur-223710) | $55,00 |
| F4 | Pxltiger Triceratops | [Asset Store 126881](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/realistic-dinosaur-triceratops-126881) | $24,00 |
| F4 | Pxltiger T. rex | [Asset Store 175294](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-t-rex-175294) | $60,00 |
| F5 | Pxltiger Brachiosaurus (ou JUNNICHI SUKO a $24,99) | [Asset Store 160378](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/brachiosaurus-giraffatitan-160378) | $60,00 |
| F5 | NatureManufacture Coast & Dunes (Z2, Z5) | [Asset Store](https://marketplace.unity.com/packages/3d/vegetation/coast-dunes-environment-dynamic-nature-beach-sea-islands-dry-des-309456) | $60,00 |
| F5, opcional | NatureManufacture L.V.E 2023 (B5; só se as cavernas voltarem) | [Asset Store](https://marketplace.unity.com/packages/tools/terrain/l-v-e-2023-lava-volcano-environment-2023-253816) | $0 a $70 |
| F5 | Cicas, samambaias arborescentes e ginkgo de qualidade (Fern Garden a $70, NÃO VERIFICADO; busca manual no Fab) | vários | ~$70 a $150 [E] |
| F5 | Props de interior: 3 a 5 pacotes, por exemplo o Laboratory PACK HDRP | [Asset Store](https://assetstore.unity.com/packages/3d/props/furniture/laboratory-pack-hdrp-181195) ($35) | $150 a $400 [E] |
| F5 | Mais áudio, se a demo mostrar que falta | — | $0 a $100 |
| Lançamento, se registrar | Marca nos EUA (USPTO), por classe | [Fish & Richardson](https://www.fr.com/insights/thought-leadership/blogs/new-uspto-trademark-fees-go-into-effect-january-18-2025/) | $350 [V, fonte secundária] |
| Sempre | Unity Personal: grátis abaixo de $200 mil de receita por ano | [Unity](https://unity.com/products/unity-personal) | $0 [V] |
| | **Total do jogo** | | **~$720 a $1.600** [E], mais designer de som e advogado de PI, se houver |
| Opcional | Burned Wood ou Dead Tree Pack (B4) | Asset Store | $24,99 / $26,99 |
| Opcional | NatureManufacture Forest Environment (B1, B2) | [Asset Store](https://marketplace.unity.com/packages/3d/vegetation/forest-environment-dynamic-nature-150668) | $60,00 |
| Opcional | Modular Cave (só se houver cavernas) | [Asset Store](https://marketplace.unity.com/packages/3d/environments/dungeons/modular-cave-ancient-cave-ancient-ruins-mayan-ruins-cavern-306634) | $79,99 |
| Opcional | Pxltiger Stegosaurus (8ª espécie) | [Asset Store 189558](https://assetstore.unity.com/packages/3d/characters/animals/reptiles/dinosaur-stegosaurus-189558) | $60,00 |
| Não recomendado | Biblioteca SpeedTree (assinatura desde maio de 2026; os modelos não são mais vendidos avulsos) | [Unity](https://support.unity.com/hc/en-us/articles/49699142648980-How-do-I-receive-my-SpeedTree-Library-purchase) | $999 por ano |
| Em aberto | Designer de som para as vozes das criaturas (decisão 7); advogado de PI antes do lançamento | — | NÃO VERIFICADO |

As criaturas somam **$293,99**, ou $258,98 com o saurópode mais barato [V]. A conta do total: no mínimo 294 + 100 (plantas) + 60
(costa) + 150 (props) + 15 (áudio) + 100 (Steam); no máximo 294 + 190 + 130 + 400 + 150 + 100 + 350 (uma classe de marca).

**Áudio é metade do terror, e hoje é zero.** O projeto não tem nenhum arquivo de áudio [M]. A demo precisa de vozes para 3 ou 4
espécies, fogo, arrebentação, vento, passos em 6 superfícies ou mais, o gerador, o rádio, o bipe do receptor e os avisos da matriz 6.5.
As vozes dos filmes estão proibidas (4.9), então os pacotes são matéria-prima: cada espécie ganha um conjunto próprio em camadas
(altura, ritmo, mistura de duas fontes), e nenhuma espécie usa um chamado de estoque sem mudança. A linha de áudio custa 3 a 5
agentes-dia na demo e 8 a 15 no jogo todo [E].

### 8.5 Testes e ferramentas

- **Menu de depuração (F1a):** começar no passo N com as linhas X feitas; teleportar para qualquer local; criar e tirar criaturas;
  uma sobreposição do diretor (valor de T, estado, territórios ativos, vagas ocupadas); modo deus; ligar e desligar os checkpoints.
- **Registrador de partidas (F3):** posição a 1 Hz, velocidade, estado do diretor, mortes com a causa, minutos por objetivo, minutos
  refeitos e o caminho feito. Os agentes analisam os registros; o dono vê as gravações.
- **Testadores:** de 8 a 10 de fora, em levas. De 3 a 5 para a demo (o dono não pode ser jogador de primeira vez, e agentes não julgam
  a sensação); 2 ou 3 por rodada da F4, cada rodada com ordens de linha diferentes; uma rodada final na F6. As três linhas em ordem
  livre dão 6 ordens estritas, e uma varredura completa custa de 18 a 30 horas de testador [E].
- **Teste cego de áudio** (fim da F3): seção 6.5.
- **Métricas que decidem:** velocidade média (decide se a ilha cresce), minutos refeitos por morte, minutos entre picos, minutos por
  passo, tempo perdido (mais de 60 s sem progresso), quantas vezes cada trecho é andado, quadros acima de 25 e 50 ms.

### 8.6 Riscos principais

| Risco | Efeito | Como se detecta | Mitigação |
|---|---|---|---|
| A margem do quadro some com as criaturas | A floresta deixa só 0,7 a 2,0 ms; skinning, penas, sombras, draws e IA não foram medidos | SD10 na F0 | O teto do ambiente sai da SD10; escala de render (SD14); densidade em bolsões; vagas do diretor |
| GameObjects em escala | 90 a 120 mil só em Z1; trancos ao ativar | SD1, SD2, SD15 | SIS-5 na F1a; teto de 6.000 por célula; SD2 como portão duro |
| Animação dos corpos comprados | Pés flutuando, pés escorregando, clipes faltando, sem câmera de morte | SD17 e a matriz de cobertura na F0 | Comprar três corpos antes; 4 a 8 agentes-dia por predador; reserva para o compy |
| Esconder vira a estratégia dominante | O terror some depois da primeira hora | Registrador: tempo escondido por partida | Checagem de esconderijo com motivo e aviso |
| Morrer custa caro demais | Jogadores desistem | Minutos refeitos no registrador | Checkpoints silenciosos, adiados até a hora segura |
| Plantas modeladas destoam dos scans | O realismo da floresta cai nas grotas e na savana | E2 | Plano B: B2 como ravina de B1; savana com a samambaia que passar na captura |
| Camadas de terreno | Bordas feias nos tiles com recuo | Capturas de T(-2,1), T(-1,0), T(1,-2) | Shader Graph de terreno com `Texture2DArray` |
| NavMesh que não vê o que o jogador vê | O T. rex entra num refúgio "seguro" | Auditoria GG = 0 nos refúgios | Fontes por tipo; caules finos contam para G e GG |
| Interiores | Luz do céu e névoa dentro dos prédios; custo por sala desconhecido | SD12 (Quonset) | No máximo 6 interiores herói; Volume local por interior; APV se preciso |
| Memória por kit de bioma | Problema no PS5; trancos de textura nas fronteiras | SD5 | No máximo 2 biomas por fronteira; 2K; camadas compartilhadas |
| O conteúdo dos pacotes não foi conferido | Kits de costa, lava e plantas podem decepcionar | Importação | Comprar só na fase da zona; preferir CC0 |
| Plataforma | A demo roda bem só no Mac | SD19 | Decisão 3 na F0; teto por plataforma |
| Jurídico: nomes, designs e sons | Um aviso da Universal | Busca de marca na F0; revisão | Tabela 4.9; vozes próprias em camadas; advogado antes do lançamento |
| Velocidade furtiva diferente de 0,6 a 1,0 m/s | A ilha fica grande ou pequena demais | Registrador da demo | Reabrir Z3 ou o farol, ou cortar rotas; o contorno não muda |
| Estimativas de esforço | O jogo completo leva mais que 19 meses | G-replan | Alavancas da seção 8.2 |
| Água | O mar sai caro ou exige jogabilidade | Medição no porto (Z2) | Falésias, barreira na altura do joelho, plano barato |

---

## 9. Decisões em aberto

Só as perguntas que precisam do dono, em ordem do que bloqueia primeiro. Cada uma traz a recomendação. As seis primeiras bloqueiam a
demo.

1. **História e jogador (bloqueia a F3: os nomes entram nos documentos).** A moldura é a **A, Quarentena** (Fallow Island ou Ilha
   Pousio, Pellbrook Life Sciences, ATAVUS), a B (O abate) ou a C (O longo silêncio)? O jogador é **o pescador náufrago**
   (recomendado), o técnico mandado para apagar os servidores, um piloto ou um médico? A busca nas bases gratuitas de marca (USPTO,
   INPI, EUIPO) roda na F0, antes de qualquer nome entrar numa captura.
2. **Cenário e plantas (bloqueia a E2, na F0).** Aceita a ilha vulcânica fria do Pacífico Norte com núcleo geotérmico, em que a flora
   mesozoica plantada pela empresa se espalha pelo solo quente? Aceita **savana de samambaias** no chão aberto, com grama só nas
   instalações e na costa? Aceita **plantas modeladas** (não escaneadas) para samambaias arborescentes, cicas, cavalinhas e
   araucárias, se passarem na captura E2 (~$40)?
3. **Para quem é a demo, e com quanto tempo do dono (bloqueia a F0).** Ela vai ser **(a)** uma demo pública na Steam, **(b)** um pitch
   para editora ou financiamento, ou **(c)** só interna? A (a) exige uma build Windows, uma medição num PC de gama média a 1080p (95 %
   da Steam é Windows), os $100 da Steam Direct e a página da loja. **Recomendação: planejar a (a)** e medir o Windows cedo, porque o
   HDRP no DX12 pode mudar a decisão 6. Há um PC Windows de gama média para medir? E quantas horas por semana o dono tem para revisões
   e playtests? O plano supõe de 50 a 115 h no total, e esse número vira o calendário.
4. **Conteúdo da demo (bloqueia a F2).** Compys primeiro, **o Dilophosaurus como clímax na volta com o galão e os raptors como gancho
   visto pela janela** (recomendado)? A alternativa é um trecho de 2 a 4 min com um raptor evitável no pátio da Estação depois do O3,
   com o `RaptorAI` de hoje: mostra o antagonista jogando, mas soma tempo e uma morte possível no minuto 28. A manada só ouvida, o
   início na falésia com o plano de mar mínimo e nenhuma caverna?
5. **Os raptors (bloqueia a compra na F0).** **Deinonychus com penas**, com o nome "Deinonychus" no jogo e "raptors" na boca da equipe
   (recomendado), ou um "Velociraptor" escamoso (Rifat_Bilalov, $29,99), sabendo que o animal real tinha uns 2 m e 15 kg?
6. **Teto de desempenho (bloqueia a F2).** Aceita que o teto do ambiente saia das medições da F0 (12,5 ms menos o custo medido das
   criaturas, menos 1,5 ms de reserva)? Se a mata densa não couber, qual alavanca:
   - **(a) densidade D2 só nos bolsões de furtividade**, até 30 % de B1, com uma mata mais leve no resto (recomendado se a escala de
     render render menos de 0,8 ms);
   - (b) escala de render 0,85 com TAA Upscale ou STP, se as capturas passarem;
   - (c) aceitar B1 a 11,0 a 11,5 ms com no máximo 1 predador materializado nas células mais densas;
   - (d) corte a 40 m (-0,67 ms) ou motion vectors desligados no LOD1 dos abetos jovens (-0,45 ms).
7. **Áudio (bloqueia a F3).** As vozes das criaturas saem de bibliotecas em camadas feitas pelos agentes (Sonniss de graça, mais uma
   biblioteca de vozes de $15 a $150; recomendado para a demo), ou de um designer de som contratado (preço NÃO VERIFICADO; pedir
   orçamento)? Documentos só em texto e o rádio com legendas (recomendado), ou voz gravada?
8. **Regras de jogo (bloqueia a F3).** Aceita o pacote das seções 6.1 a 6.10?
   - os sentidos novos: cheiro com vento que muda na noite, ruído grave para o T. rex, visibilidade do facho por estado;
   - a lanterna em três estados (Alta, Baixa, Apagada), que nunca acaba;
   - a checagem de esconderijo com motivo e aviso a partir do passo 2a, e o verbo de prender a respiração;
   - morte de um golpe pelos caçadores, com a regra do primeiro contato e o ferimento sem HUD;
   - salvamentos automáticos silenciosos mais os manuais nos abrigos (em vez de só manuais, como em *Alien: Isolation*);
   - raptors não abrem portas, e porta estreita barra raptor mas não compy;
   - o receptor (chicote, depois Yagi) como ferramenta principal, e nenhum item não letal, como um dardo tranquilizante, para manter o
     "quase imbatível".

   Recomendação: aceitar o pacote todo.
9. **Tamanho, ritmo e esforço (bloqueia a F4).** Aceita uns 120 ha jogáveis, que só crescem se o ritmo medido na demo pedir; **uma
   noite em quatro atos** com cinco passos de tensão, o relógio andando a cada objetivo (recomendado), em vez de três noites; e o
   total de **174 a 337 agentes-dia (9 a 19 meses)**, com o G-replan depois da demo? Se for longo demais, qual alavanca da seção 8.2?
10. **O final (bloqueia a F4).** O chamado no cume abre o ato 4, e o resgate é **de helicóptero na pista acesa ao amanhecer, com a
    espera do lado de fora** (recomendado)? As alternativas são o chamado como final (perto de *Trespasser*) ou um heliponto no cume.
11. **Escopo menor e repositório (sem pressa).**
    - O **Gallimimus** fica adiado (recomendado) ou entra no lugar do Parasaurolophus?
    - Qual saurópode: Pxltiger Brachiosaurus a $60 (recomendado), *B. altithorax* a $24,99 ou Diplodocus a $55?
    - **Cavernas** e o **túnel de serviço** adiados para depois do lançamento (recomendado)?
    - Os **2 a 3 GB gerados** ficam fora do git, com um manifesto de hashes por tile no git e builds publicadas só da máquina de build
      (recomendado)?

---

## Notas da revisão

As três revisões de 2026-10-22 (`docs/design/ilha-trabalho/reviews/`) tinham 64 achados: 4 críticos, 20 altos, 35 médios e 5 baixos. Todos os
críticos e altos foram aceitos, alguns com adaptação, e todos os médios e baixos foram tratados. Onde cada um entrou: escopo 1 na
seção 8.2; escopo 2 a 5 nas seções 7.4, 7.8, 8.5, 5.1 e 8.4; terror 1 e 2 nas seções 6.1 e 6.9; terror 3 a 10 nas seções 6.6, 6.5,
4.5, 5.4, 5.8, 4.7, 6.3 e 5.8 (CE14); técnica 1 a 9 nas seções 7.1, 3.10, 7.4, 7.8 (SD11), 3.1, 6.1, 7.5 e 7.6. O mapa mudou junto
(`map-spec.json` v2, `meta.changes`).

**Onde este documento diverge de um achado, e por quê:**

1. **Escopo 7, um trecho jogável de raptor no Portão da Mata: adaptado.** O raptor aparece como gancho visto pela janela (terror 25).
   O portão fica a uns 320 m do SR1, de 6 a 8 minutos a pé, então não cabe em 2 a 4 minutos; uma morte no minuto 28 custaria refazer o
   fim da demo; e o ciclo "o facho te denuncia" já é ensinado pelo Dilophosaurus e pelos compys. O Deinonychus comprado continua sendo
   usado (gancho, SD10, SD17). A versão jogável ficou como alternativa na decisão 4.
2. **Terror 7, a lição do T. rex na R7: lugar trocado.** O O12 só vem depois das três linhas, já no passo 3, e a R7 não é a única
   entrada do cume (há a T5 e a T6b). O hub, que as linhas B e C cruzam obrigatoriamente, foi movido para a boca do Passo Sul, e a cena
   CE7 acontece lá, no passo 2a. As rotas que pulam o hub ficam na regra do primeiro contato.
3. **Terror 21, abrir parte do túnel com o cartão 1: recusado.** O túnel foi adiado (escopo 16): 3 a 6 agentes-dia por 0,95 km, quase
   o tamanho das estradas que ele substitui (1,07 km). O deslocamento ganha vinhetas, portões de mão única e postes acesos depois do O7.
4. **Terror 23, o vento: adaptado.** A tempestade foi adiada (escopo 16), então a mudança do passo 3 é só o parâmetro de vento com 60 s
   de rajadas. E a "brisa de terra do leste" do achado está invertida: brisa de terra sopra da terra para o mar, então na costa leste
   ela vem de oeste. O ato 4 tem calmaria com brisa fraca de oeste; na pista o cheiro do jogador vai para o mar, e o final se joga com
   som e com a visão do amanhecer.
5. **Técnica 14, camada "visual" e camada simulada: adaptado.** Ficam duas camadas de IA (escopo 16). Da técnica 14 ficam o grafo de
   waypoints para as criaturas abstratas, a promoção por linha de visão de mirante e a materialização forçada nas cenas. Os herbívoros
   de spline são sempre desenhados. Uma camada visual para predadores só entra se os playtests mostrarem que eles aparecem do nada.
6. **Escopo 10, Z3 com uns 10 ha e a R8 não jogável: ajustado ao mapa.** O corredor medido dá 9,0 ha com 50 m de cada lado. A T8 saiu
   junto, porque levava à parte de Z3 que virou fundo. A R8 foi dividida em vez de cortada: a metade sul é a rota do ato 4 (o T-REX-3 a
   cobre); só a metade norte, até o farol, deixou de ser jogável.
7. **Escopo 9: estendido.** O limite de 50 a 60 m de névoa no chão vale para toda zona aberta, inclusive Z4, e não só para Z5.
8. **Terror 1 e terror 6 davam momentos diferentes para a checagem de esconderijo** (0,25 no ato 2 num, só no passo 3 no outro).
   Ficou: a partir do passo 2a, a 0,25, como o primeiro comportamento novo depois da demo; o flanco vem no passo 3.
9. **Terror 9 pedia aberturas de 1,2 m para raptor; a técnica 6 mostra que o bake não liga 1,2 m.** Ficou 1,5 m ou mais para passar,
   0,9 m ou menos para barrar, e nada entre os dois nas instalações.
10. **Terror 17, SR1 e SR6 com gerador:** só o SR1 usa gerador. O SR6 se garante com bateria, como os outros, e o ruído do final é o
    gerador da pista (O13).
11. **Terror 24, o T. rex a 8 m/s:** aceito como valor de jogo, mas fica acima dos 5 a 7,5 m/s dos estudos de osso e músculo citados
    na seção 4.8. A fuga vem dos refúgios a até 60 m, não de correr mais que ele.
12. **Terror 5 (o primeiro bote não mata) virou uma regra geral, a do primeiro contato**, também para o T. rex e o Triceratops. Ela
    cobre as rotas que pulam as cenas de apresentação (escopo 12). Os raptors ficam de fora, porque as cenas deles são obrigatórias.
13. **Terror 13, o receptor:** em vez de "o ritmo dá a distância", ficou a convenção real: o volume dá a faixa de distância e o ritmo
    dá o estado do animal (ADF&G). A Yagi entra como proposto.
14. **Nomes de sonda e de cena:** o escopo 4 e a técnica 4 deram o nome "S11" a duas sondas novas, e os números S10 a S19 colidem com
    os ids dos locais. As sondas viraram SD1 a SD19 (as da v1 mantêm o número: S1 é a SD1), com a travessia na SD11 e o interior na
    SD12. As cenas são CE1 a CE15, porque E2 já é o teste de plantas.
15. **Escopo 1, o total:** 174 a 337 agentes-dia, acima das duas recontagens (140 a 330 e, cortado, 110 a 260 no escopo; 100 a 170 na
    técnica), porque esta conta soma o que as três revisões pediram juntas. A seção 8.2 mostra cada linha.
16. **Escopo 6, todo o comportamento na F4:** aceito, com uma exceção: o Brachiosaurus só anda numa spline, então a F4 usa uma cápsula
    e o modelo é comprado na F5.

**O que mudou sem achado:**

- **O SR4 abre no passo 2a.** A v1 dizia que o abrigo mais próximo ficava a no máximo 382 m de qualquer rota, mas o SR4 só abria depois
  do O7: antes disso a R7 ficava a 651 m do abrigo aberto mais próximo. Abrir o SR4 cedo também corta o pior caso de morte do terror
  2 (o O7 inteiro dentro do território do bando B).
- **Três áreas de território estavam erradas no JSON v1** (DEIN-A, DEIN-A3 e DEIN-B; seção 7.3). Os polígonos não mudaram de tamanho;
  só os números. O "cerca de 80 ha de território de raptor" do terror 1 vira uns 62 ha na v1 (o argumento do achado continua valendo).
- **As paletas de terreno v2 bastaram** para a técnica 2 (com o recuo em 3 tiles), e o Shader Graph de terreno ficou como plano B.

---

## Fontes

**Registros do projeto:** `docs/design/ilha-trabalho/research/biomes.md`, `species.md`, `open-world.md` e `facility.md`;
`docs/design/ilha-trabalho/reviews/scope.md`, `horror.md` e `technical.md`, com `reviews/tools/tile-layers.py`;
`docs/design/ilha-trabalho/tools/tile-layers-v2.py`; `docs/floresta/spec.md`, `build/STATE.md` e `build/issues/D2-density.md`,
`N1-night.md`, `W1-runtime.md` e `M3-young-firs.md`. Código: `Assets/Game/Editor/Forest/ForestLayout.cs`, `ForestContracts.cs` e
`ForestLightingPresets.cs`; `Assets/Game/Scripts/World/Foliage/ForestWind.cs`, `ForestWindGlobals.cs`, `ForestWindInstances.cs`,
`FoliageInteractionField.cs` e `FoliagePusher.cs`; `Scripts/Player/PlayerController.cs` e `PlayerFlashlight.cs`;
`Scripts/Dinosaurs/RaptorAI.cs`; `Scripts/World/HidingSpot.cs`; `Scripts/Core/NoiseSystem.cs` e `GameSession.cs`;
`Packages/manifest.json`. O mapa é `docs/design/ilha-mapa.json` (v2).

**Cenário e botânica:**
[Kunashir Island](https://en.wikipedia.org/wiki/Kunashir_Island) ·
[NZPCN: geothermal plants](https://nzpcn.org.nz/ecosystems/plant-communities/scrub-shrublands/geothermal/geothermal-plants) ·
[NZPCN: Dicranopteris linearis](https://www.nzpcn.org.nz/flora/species/dicranopteris-linearis/) ·
[RHS: Dicksonia antarctica](https://www.rhs.org.uk/plants/5794/wd/details) ·
[Paleobiota of the Morrison Formation](https://en.wikipedia.org/wiki/Paleobiota_of_the_Morrison_Formation) ·
[USGS Morrison](https://pubs.usgs.gov/publication/70027121) ·
[Carnarvon Gorge](https://en.wikipedia.org/wiki/Carnarvon_Gorge) ·
[University of Melbourne: samambaias arborescentes e fogo](https://findanexpert.unimelb.edu.au/news/12942-tree-ferns-are-older-than-dinosaurs.-and-that's-not-even-the-most-interesting-thing-about-them).
As fotos de referência de cada bioma (categorias do Wikimedia Commons) estão em `biomes.md` seção 4.

**Espécies:**
[Compsognathus](https://en.wikipedia.org/wiki/Compsognathus) · [Dilophosaurus](https://en.wikipedia.org/wiki/Dilophosaurus) ·
[Velociraptor](https://en.wikipedia.org/wiki/Velociraptor) · [Deinonychus](https://en.wikipedia.org/wiki/Deinonychus) ·
[Triceratops](https://en.wikipedia.org/wiki/Triceratops) · [Parasaurolophus](https://en.wikipedia.org/wiki/Parasaurolophus) ·
[Brachiosaurus](https://en.wikipedia.org/wiki/Brachiosaurus) · [Tyrannosaurus](https://en.wikipedia.org/wiki/Tyrannosaurus) ·
[National Geographic sobre o Dilophosaurus (2020)](https://www.nationalgeographic.com/science/article/jurassic-park-got-almost-everything-wrong-about-iconic-dinosaur-dilophosaurus) ·
[NPR sobre Schmitz e Motani (2011)](https://www.npr.org/2011/04/14/135385317/dinosaur-eyes-yield-clues-to-hunting-habits) ·
[Science News sobre Stevens (2006)](https://www.sciencenews.org/article/sight-saur-eyes-t-rex-vision-was-among-natures-best) ·
[ScienceDaily sobre Cullen et al. (2023)](https://www.sciencedaily.com/releases/2023/03/230330172120.htm) ·
[Sandia: a crista do Parasaurolophus (1997)](https://www.sandia.gov/labnews/1997/12/19/dinosaur-story/) ·
[Magrath et al. 2015](https://ro.uow.edu.au/smhpapers/2748). Os links dos modelos estão nas seções 4 e 8.4.

**Instalação e mundo real:**
[NatGeo: sangue de jacaré (2008)](https://www.nationalgeographic.com/animals/2008/04/alligator-blood-antibiotics) ·
[GEN](https://genengnews.com/topics/drug-discovery/its-no-crock-that-alligator-peptides-act-as-potent-antibacterials) ·
[NEH sobre Adak](https://www.neh.gov/humanities/2017/winter/statement/adak-island-far-out-in-the-bering-sea-may-be-where-the-wind-begins) ·
[US Navy BRAC: Adak](https://www.bracpmo.navy.mil/BRAC-Bases/Other-West/Former-Naval-Air-Facility-Adak/) ·
[GAO sobre Plum Island](https://www.gao.gov/assets/a95284.html) ·
[Texas A&M: cercas elétricas](https://agrilifeextension.tamu.edu/library/wildlife-nature-environment/deterring-bears-with-electrified-fences-a-beginners-guide) ·
[ADF&G: coleiras VHF](https://www.adfg.alaska.gov/static/home/library/pdfs/wildlife/research_pdfs/alces/5197.pdf) ·
[Perdix: antena Yagi VHF para telemetria](https://perdixwildlifesupplies.com/products/perdix-vhf-yagi-antenna) ·
[RadioReference: rastreio direcional (informal)](https://forums.radioreference.com/threads/directional-tracking.247573/) ·
[Flight Light: FAA L-864](https://flightlight.com/what-is-an-faa-l-864-obstruction-light/) ·
[Light characteristic](https://en.wikipedia.org/wiki/Light_characteristic) ·
[Aerodrome beacon](https://en.wikipedia.org/wiki/Aerodrome_beacon) ·
[UF/IFAS: brilho dos olhos](https://edis.ifas.ufl.edu/publication/UW198) ·
[Ethology 2017](https://bio.acousti.ca/node/52564).

**Design de jogo:**
[Game Developer: a IA de Alien: Isolation](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation) ·
[Revisiting the AI of Alien: Isolation](https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation) ·
[Shacknews: prévia de Alien: Isolation](https://shacknews.com/article/83607/alien-isolation-preview-extra-terrifying) ·
[VGChartz: análise de Alien: Isolation](https://www.vgchartz.com/game/77802/alien-isolation/reviews/254064/) ·
[Fórum da Steam sobre os armários de Alien: Isolation (NÃO VERIFICADO)](https://steamcommunity.com/app/214490/discussions/0/3109144584165201158) ·
[Booth, The AI Systems of Left 4 Dead (2009)](https://steamcdn-a.akamaihd.net/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf) ·
[GDC: The Design of Subnautica](https://gdcvault.com/play/1025745/The-Design-of-Subnautica) ·
[Amnesia: The Bunker](https://en.wikipedia.org/wiki/Amnesia:_The_Bunker) ·
[GMTK sobre Breath of the Wild](https://gmtk.substack.com/p/how-nintendo-solved-zeldas-open-world) ·
[Trespasser](https://en.wikipedia.org/wiki/Trespasser_(video_game)).

**Produção, áudio e lojas:**
[Sonniss GDC Game Audio Bundle: licença](https://sonniss.com/gameaudiogdc) (conferido em 2026-10-22) ·
[Magic Sound Effects: Dinosaur Vocalizations](https://magicsoundeffects.itch.io/dinosaur-vocalizations) (conferido em 2026-10-22) ·
[Steamworks: Steam Direct](https://partner.steamgames.com/doc/gettingstarted/appfee) (conferido em 2026-10-22) ·
[Steam Hardware & Software Survey](https://store.steampowered.com/hwsurvey/) ·
[Unity Personal](https://unity.com/products/unity-personal) ·
[Fish & Richardson: taxas da USPTO desde 2025-01-18](https://www.fr.com/insights/thought-leadership/blogs/new-uspto-trademark-fees-go-into-effect-january-18-2025/) ·
[Asset Store: Laboratory PACK HDRP](https://assetstore.unity.com/packages/3d/props/furniture/laboratory-pack-hdrp-181195).

**Jurídico e licenças de lojas:**
[Inside the Magic: Cretaceous Kingdom (2024)](https://insidethemagic.net/2024/05/jurassic-park-fan-made-game-cretaceous-kingdom-dr1/) ·
[FAQ de licença do Sketchfab](https://help.sketchfab.com/en/articles/16152225-store-license-usage-faq) ·
[Licenças do Fab](https://dev.epicgames.com/documentation/en-us/fab/licenses-and-pricing-in-fab) ·
[FAQ da EULA da Asset Store](https://assetstore.unity.com/browse/eula-faq) ·
[Unity: SpeedTree Library](https://support.unity.com/hc/en-us/articles/49699142648980-How-do-I-receive-my-SpeedTree-Library-purchase).

**Unity e animação:**
[Animancer: two-bone IK (o IK nativo é só Humanoid)](https://kybernetik.com.au/animancer-v7-4/docs/examples/jobs/two-bone-ik) ·
[Issue tracker: Two Bone IK com deslocamento em rigs Generic](https://issuetracker.unity.com/issues/9969/animation-rigging-two-bone-ik-gets-offset-when-a-generic-rig-avatar-is-created-from-an-existing-model) ·
[Unity Discussions: limite do NavMesh.CalculatePath](https://discussions.unity.com/t/is-there-a-cap-on-navmesh-calculatepath/177160) ·
[backgroundLoadingPriority](https://docs.unity3d.com/ScriptReference/Application-backgroundLoadingPriority.html) ·
[NavMeshBuildSettings.voxelSize](https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings-voxelSize.html) ·
[GraphicsStateCollection](https://docs.unity3d.com/ScriptReference/Rendering.GraphicsStateCollection.html) ·
[Mesh LOD no Unity 6.6](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html).

**Unity (documentação local da 6000.6.0f1 e do HDRP 17.6.0):** `Manual/WorldBuildingFeature.html`,
`ScriptReference/Application-backgroundLoadingPriority.html`, `ScriptReference/AI.NavMeshBuildSettings-voxelSize.html`,
`ScriptReference/Rendering.GraphicsStateCollection.html`, `Manual/TextureStreaming-introduction.html`,
`ScriptReference/AI.NavMeshBuildSourceShape.html`, `AI.NavMeshBuilder.UpdateNavMeshDataAsync.html`; HDRP `terrain-lit-material.md`,
`terrain-master-stack-reference.md`, `gpu-resident-drawer.md`, `local-volumetric-fog-volume-reference.md`,
`create-a-local-fog-effect.md`, `fog-volume-override-reference.md`, `Dynamic-Resolution.md`, `probevolumes-streaming.md`,
`water-quality-and-performance-decisions.md`; AI Navigation `NavMeshLink.md`.
