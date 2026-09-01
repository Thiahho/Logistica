/**
 * Comprime la foto de la prueba de entrega antes de enviarla (construccion_v1.md §7: lado largo
 * 1280 px, JPEG calidad 0.7, objetivo < 200 KB). El backend (AlmacenamientoFotos) corta en 400 KB
 * por foto y 2 MB de request total, y exige magic bytes JPEG — de ahí el "image/jpeg" fijo abajo:
 * una foto de cámara sin comprimir casi siempre supera esos límites.
 */
export async function comprimirFoto(archivo: File, ladoMaximo = 1280, calidad = 0.7): Promise<Blob> {
  const bitmap = await createImageBitmap(archivo);
  const escala = Math.min(1, ladoMaximo / Math.max(bitmap.width, bitmap.height));
  const ancho = Math.round(bitmap.width * escala);
  const alto = Math.round(bitmap.height * escala);

  const canvas = document.createElement("canvas");
  canvas.width = ancho;
  canvas.height = alto;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("No se pudo preparar la foto para enviar.");
  ctx.drawImage(bitmap, 0, 0, ancho, alto);
  bitmap.close();

  return await new Promise<Blob>((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob ? resolve(blob) : reject(new Error("No se pudo comprimir la foto."))),
      "image/jpeg",
      calidad,
    );
  });
}
