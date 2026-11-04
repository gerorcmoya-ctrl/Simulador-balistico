# Simulador-balistico
Simulador en el que el jugador regula ángulo, fuerza y masa de un proyectil para derribar estructuras armadas con Rigidbodies y Joints. Disparo y colisiones están gobernados por el sistema de físicas de Unity. Al final de cada intento se muestra la puntuación y un reporte de tiro.
Cómo jugar
Clonar el repositorio y abrirlo con Unity Hub (Add project from disk).
Abrir cualquier escena (puede ser una vacía) y presionar Play. No hay que arrastrar nada: BallisticSimulator se inicia solo y construye la escena (suelo, cañón, objetivos, UI).
Ajustar los controles, mirar la línea amarilla (trayectoria prevista) y presionar DISPARAR.
Esperar el reporte de tiro (~4 s después del primer impacto).
Usar REINICIAR para reconstruir las estructuras y volver a intentar.
Controles
Control	Rango	Efecto
Slider Ángulo	5° – 85°	Inclinación del cañón
Slider Fuerza	10 – 300 N	Fuerza que actúa a lo largo del cañón (3 m)
Slider Masa	1 – 10 kg	Masa del Rigidbody del proyectil
Botón DISPARAR	–	Lanza el proyectil (deshabilitado mientras dura el intento)
Botón REINICIAR	–	Reconstruye las estructuras
VIDEO DE YT
https://www.youtube.com/watch?v=sWRGt1jVixY
