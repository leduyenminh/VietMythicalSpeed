// Password-reset email in each of the game's languages.
import { LANGUAGES, type Language } from '../game/types.ts';

export function languageOf(value: unknown, fallback: Language = 'vi'): Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value) ? (value as Language) : fallback;
}

export function resetEmail(lang: Language, name: string, code: string): { subject: string; text: string } {
  const sign = '\n\nViet Mythical Speed';
  switch (lang) {
    case 'en':
      return { subject: 'Your Viet Mythical Speed reset code', text: `Hi ${name},\n\nYour password reset code is: ${code}\nIt expires in 15 minutes. If you didn't ask for this, ignore this email.${sign}` };
    case 'zh':
      return { subject: 'Viet Mythical Speed 密码重置验证码', text: `${name}，你好：\n\n你的密码重置验证码是：${code}\n验证码在 15 分钟内有效。如果这不是你本人的操作，请忽略此邮件。${sign}` };
    case 'ja':
      return { subject: 'Viet Mythical Speed パスワード再設定コード', text: `${name} さん\n\nパスワード再設定コード：${code}\nこのコードの有効期限は15分です。お心当たりがない場合は、このメールを破棄してください。${sign}` };
    case 'ko':
      return { subject: 'Viet Mythical Speed 비밀번호 재설정 코드', text: `${name}님, 안녕하세요.\n\n비밀번호 재설정 코드: ${code}\n이 코드는 15분 동안 유효합니다. 요청하지 않으셨다면 이 메일을 무시해 주세요.${sign}` };
    case 'es':
      return { subject: 'Tu código para restablecer la contraseña de Viet Mythical Speed', text: `Hola, ${name}:\n\nTu código para restablecer la contraseña es: ${code}\nCaduca en 15 minutos. Si no lo has solicitado, ignora este correo.${sign}` };
    case 'de':
      return { subject: 'Dein Code zum Zurücksetzen des Passworts für Viet Mythical Speed', text: `Hallo ${name},\n\ndein Code zum Zurücksetzen des Passworts lautet: ${code}\nEr ist 15 Minuten lang gültig. Wenn du das nicht angefordert hast, ignoriere diese E-Mail einfach.${sign}` };
    case 'fi':
      return { subject: 'Viet Mythical Speed -salasanan palautuskoodi', text: `Hei ${name},\n\nsalasanasi palautuskoodi on: ${code}\nKoodi on voimassa 15 minuuttia. Jos et pyytänyt sitä, voit jättää tämän viestin huomiotta.${sign}` };
    default:
      return { subject: 'Mã đặt lại mật khẩu Viet Mythical Speed', text: `Xin chào ${name},\n\nMã đặt lại mật khẩu của bạn là: ${code}\nMã có hiệu lực trong 15 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.${sign}` };
  }
}
