using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace XRServer.Licensing
{
    /// <summary>
    /// license.lic 全体のJSON構造
    /// 既存Unity版との互換のため、フィールド名はそのまま維持
    /// </summary>
    public class LicenseItem
    {
        public string name { get; set; }
        public string expireDate { get; set; }
        public string MACAddress { get; set; }   // 実態は Device ID 用の欄として利用
        public string signed { get; set; }
        public int userAmmount { get; set; }
        public bool CADImport { get; set; }

    }

    /// <summary>
    /// signed を復号した中身
    /// 既存Unity版との互換のため、フィールド名はそのまま維持
    /// </summary>
    public class EncryptedSign
    {
        public string name { get; set; }
        public string expireDate { get; set; }
        public string MACAddress { get; set; }   // 実態は Device ID 用の欄として利用
        public int userAmmount { get; set; }
        public bool CADImport { get; set; }
    }

    /// <summary>
    /// 検証結果
    /// UI依存を持たせず、呼び出し側で MessageBox / ログ / 終了処理を行う
    /// </summary>
    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public LicenseItem License { get; set; }
        public EncryptedSign SignedPayload { get; set; }
        public string CurrentDeviceId { get; set; }
    }

    /// <summary>
    /// Unity依存を外した Windows 用ライセンスローダ
    /// </summary>
    public static class LicenseLoaderWin
    {
        // 既存Unity版に合わせる
        // 生成アプリ側も同じ IV / Key に揃えること
        private const string AES_IV = "kukimotonobuyuki";
        private const string AES_Key = "vrliteatinde2022";

        /// <summary>
        /// ライセンスファイルを読み込んで検証する
        /// </summary>
        /// <param name="filePath">license.lic のパス</param>
        public static LicenseValidationResult LoadAndValidate(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return Fail("ライセンスファイルが存在しません。");
                }

                // license.lic を読み込む
                string dataStr = File.ReadAllText(filePath, Encoding.UTF8);

                // JSON を LicenseItem に変換
                LicenseItem license = JsonConvert.DeserializeObject<LicenseItem>(dataStr);
                if (license == null)
                {
                    return Fail("ライセンスファイルの読み込みに失敗しました。");
                }

                if (string.IsNullOrWhiteSpace(license.signed))
                {
                    return Fail("ライセンスファイルに signed がありません。");
                }

                // signed を復号
                string decrypted = Decrypt(license.signed, AES_IV, AES_Key);

                // 復号結果を EncryptedSign に変換
                EncryptedSign payload = JsonConvert.DeserializeObject<EncryptedSign>(decrypted);
                if (payload == null)
                {
                    return Fail("signed の復号結果を読み取れませんでした。");
                }

                // 表側 JSON と signed 内の主要項目が一致するか確認
                // ここが一致しない場合はファイル改ざんの可能性が高い
                if (!StringEquals(license.name, payload.name))
                {
                    return Fail("ライセンス name が一致しません。");
                }

                if (!StringEquals(license.expireDate, payload.expireDate))
                {
                    return Fail("ライセンス expireDate が一致しません。");
                }

                if (!StringEquals(NormalizeId(license.MACAddress), NormalizeId(payload.MACAddress)))
                {
                    return Fail("ライセンス Device ID が一致しません。");
                }

                // 現在の Windows Device ID を取得
                string currentDeviceId = GetWindowsDeviceId();
                if (string.IsNullOrWhiteSpace(currentDeviceId))
                {
                    return Fail("Windows Device ID を取得できませんでした。");
                }

                // ライセンス記載のIDと、このPCのIDを比較
                if (!StringEquals(NormalizeId(payload.MACAddress), NormalizeId(currentDeviceId)))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "申請したデバイスIDとこのPCのデバイスIDが異なります。",
                        License = license,
                        SignedPayload = payload,
                        CurrentDeviceId = currentDeviceId
                    };
                }

                // userAmmount も改ざんチェック
                //if (license.userAmmount != payload.userAmmount)
                //{
                //    MessageBox.Show(license.userAmmount + " " + payload.userAmmount);
                //    return Fail("ライセンス userAmmount が一致しません。");
                //}

                //// CADImport も改ざんチェック
                //if (license.CADImport != payload.CADImport)
                //{
                //    return Fail("ライセンス CADImport が一致しません。");
                //}

                // 有効期限チェック
                if (!ExpireDateCheck(payload.expireDate, out string dateError))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = dateError,
                        License = license,
                        SignedPayload = payload,
                        CurrentDeviceId = currentDeviceId
                    };
                }

                // ここまで通れば有効
                return new LicenseValidationResult
                {
                    IsValid = true,
                    ErrorMessage = "",
                    License = license,
                    SignedPayload = payload,
                    CurrentDeviceId = currentDeviceId
                };
            }
            catch (Exception ex)
            {
                return Fail("ライセンス検証中に例外が発生しました: " + ex.Message);
            }
        }

        /// <summary>
        /// license.lic を単純に読み込むだけ
        /// 検証はしない
        /// </summary>
        public static LicenseItem LoadLicense(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string dataStr = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<LicenseItem>(dataStr);
        }

        /// <summary>
        /// Windows の Device ID を取得
        /// まず MachineGuid を使用
        /// Unity の deviceUniqueIdentifier と完全一致するわけではないので、
        /// 以後 XRServer 用ライセンス生成時はこの値を入れる前提で運用する
        /// </summary>
        public static string GetWindowsDeviceId()
        {
            try
            {
                // 64bit / 32bit 差異を避けるため RegistryView を明示
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    string machineGuid = subKey?.GetValue("MachineGuid")?.ToString();
                    if (!string.IsNullOrWhiteSpace(machineGuid))
                    {
                        return machineGuid.Trim();
                    }
                }
            }
            catch
            {
                // 64bit 取得に失敗したら次で再試行
            }

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    string machineGuid = subKey?.GetValue("MachineGuid")?.ToString();
                    if (!string.IsNullOrWhiteSpace(machineGuid))
                    {
                        return machineGuid.Trim();
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        /// <summary>
        /// ライセンスの有効期限チェック
        /// 既存Unity版は yyyy/MM/dd 前提なのでそれに合わせる
        /// </summary>
        public static bool ExpireDateCheck(string dateStr, out string errorMessage)
        {
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(dateStr))
            {
                errorMessage = "有効期限が空です。";
                return false;
            }

            if (!DateTime.TryParseExact(
                    dateStr,
                    "yyyy/MM/dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime expiresDate))
            {
                errorMessage = "有効期限の形式が不正です。yyyy/MM/dd 形式である必要があります。";
                return false;
            }

            if (expiresDate.Date >= DateTime.Today)
            {
                return true;
            }

            errorMessage = $"有効期限が切れています。有効期限: {expiresDate:yyyy/MM/dd}";
            return false;
        }

        /// <summary>
        /// 文字列を AES 暗号化
        /// 生成アプリ側でも利用可能
        /// </summary>
        public static string Encrypt(string text, string iv, string key)
        {
            using (RijndaelManaged rijndael = new RijndaelManaged())
            {
                rijndael.BlockSize = 128;
                rijndael.KeySize = 128;
                rijndael.Mode = CipherMode.CBC;
                rijndael.Padding = PaddingMode.PKCS7;

                rijndael.IV = Encoding.UTF8.GetBytes(iv);
                rijndael.Key = Encoding.UTF8.GetBytes(key);

                ICryptoTransform encryptor = rijndael.CreateEncryptor(rijndael.Key, rijndael.IV);

                byte[] encrypted;
                using (MemoryStream mStream = new MemoryStream())
                {
                    using (CryptoStream ctStream = new CryptoStream(mStream, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter sw = new StreamWriter(ctStream))
                        {
                            sw.Write(text);
                        }
                        encrypted = mStream.ToArray();
                    }
                }
                return Convert.ToBase64String(encrypted);
            }
        }

        /// <summary>
        /// AES 暗号文字列を復号
        /// </summary>
        public static string Decrypt(string cipher, string iv, string key)
        {
            using (RijndaelManaged rijndael = new RijndaelManaged())
            {
                rijndael.BlockSize = 128;
                rijndael.KeySize = 128;
                rijndael.Mode = CipherMode.CBC;
                rijndael.Padding = PaddingMode.PKCS7;

                rijndael.IV = Encoding.UTF8.GetBytes(iv);
                rijndael.Key = Encoding.UTF8.GetBytes(key);

                ICryptoTransform decryptor = rijndael.CreateDecryptor(rijndael.Key, rijndael.IV);

                using (MemoryStream mStream = new MemoryStream(Convert.FromBase64String(cipher)))
                using (CryptoStream ctStream = new CryptoStream(mStream, decryptor, CryptoStreamMode.Read))
                using (StreamReader sr = new StreamReader(ctStream))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// ID 比較用に記号を除去
        /// </summary>
        private static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            value = Regex.Replace(value, @"[^0-9a-zA-Z\-]", "");
            value = value.Replace("-", "");
            return value.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// null 安全な文字列比較
        /// </summary>
        private static bool StringEquals(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// エラー結果生成
        /// </summary>
        private static LicenseValidationResult Fail(string message)
        {
            return new LicenseValidationResult
            {
                IsValid = false,
                ErrorMessage = message,
                CurrentDeviceId = GetWindowsDeviceId()
            };
        }

    }
}