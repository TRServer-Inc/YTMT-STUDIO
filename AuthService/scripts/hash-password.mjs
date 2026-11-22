import argon2 from 'argon2';
import { StringDecoder } from 'node:string_decoder';

function readHidden(prompt) {
  if (!process.stdin.isTTY || typeof process.stdin.setRawMode !== 'function') {
    throw new Error('Bu araç etkileşimli bir terminalde çalıştırılmalıdır.');
  }

  return new Promise((resolve, reject) => {
    let value = '';
    const decoder = new StringDecoder('utf8');
    process.stdout.write(prompt);
    process.stdin.setRawMode(true);
    process.stdin.resume();

    const cleanup = () => {
      process.stdin.setRawMode(false);
      process.stdin.pause();
      process.stdin.removeListener('data', onData);
    };

    const onData = (buffer) => {
      for (const character of decoder.write(buffer)) {
        if (character === '\u0003') {
          cleanup();
          process.stdout.write('\nİşlem iptal edildi.\n');
          reject(new Error('İptal edildi.'));
          return;
        }
        if (character === '\r' || character === '\n') {
          cleanup();
          process.stdout.write('\n');
          resolve(value);
          return;
        }
        if (character === '\u0008' || character === '\u007f') {
          const characters = Array.from(value);
          if (characters.length > 0) {
            characters.pop();
            value = characters.join('');
            process.stdout.write('\b \b');
          }
          continue;
        }
        if (character >= ' ' && character !== '\u007f') {
          value += character;
          process.stdout.write('*');
        }
      }
    };

    process.stdin.on('data', onData);
  });
}

try {
  const password = await readHidden('Yeni hesap şifresi: ');
  if (Array.from(password).length < 12 || Array.from(password).length > 256) {
    throw new Error('Şifre en az 12, en fazla 256 karakter olmalıdır.');
  }

  const confirmation = await readHidden('Şifreyi tekrar gir: ');
  if (password !== confirmation) {
    throw new Error('Şifreler eşleşmiyor.');
  }

  const hash = await argon2.hash(password, {
    type: argon2.argon2id,
    memoryCost: 65_536,
    timeCost: 3,
    parallelism: 4,
    hashLength: 32,
    saltLength: 16
  });

  process.stdout.write('\nOluşturulan Argon2id hash (yalnızca bunu GitHub accounts.json dosyasına ekle):\n');
  process.stdout.write(`${hash}\n`);
} catch (error) {
  console.error(`Hata: ${error.message}`);
  process.exitCode = 1;
}