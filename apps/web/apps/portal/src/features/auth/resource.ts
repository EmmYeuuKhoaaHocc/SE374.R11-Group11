import { defineResources } from "@neuroscan/i18n";

export default defineResources({
  "en-US": {
    auth: {
      login: {
        title: "NeuroScan Clinical Portal",
        subtitle: "Sign in with VNeID verified medical credentials to continue.",
        vneidButton: "Sign in with VNeID",
        securityNote: "Session tokens are stored exclusively in HttpOnly cookies on the backend.",
        errorHeading: "Authentication Denied",
      },
      callback: {
        processingTitle: "Verifying VNeID Credentials",
        processingSubtitle: "Validating your CCHN and hospital status...",
        errorTitle: "Sign-In Could Not Be Completed",
        backToLogin: "Back to Sign In",
        missingParams: "Missing OIDC authorization code or state parameter.",
      },
      forbidden: {
        title: "Access Restricted",
        description: "Your current UserRole does not have permission to view this section.",
      },
      errors: {
        NO_CCHN_PERMISSION: "Citizen does not hold an active CCHN.",
        PRACTITIONER_SUSPENDED: "Your CCHN is suspended or inactive in the Ministry of Health registry.",
        HOSPITAL_NOT_ONBOARDED: "Your hospital is not registered or active on NeuroScan.",
        USER_SUSPENDED: "Your practitioner User has been suspended by an administrator.",
        INVALID_OIDC_STATE: "Your login session expired or could not be verified. Please sign in again.",
        INVALID_OIDC_NONCE: "Security verification failed during OIDC sign-in. Please try again.",
        INVALID_ID_TOKEN: "Identity token verification failed.",
        OIDC_TOKEN_EXCHANGE_FAILED: "Failed to exchange VNeID authorization code.",
        OIDC_JWKS_UNAVAILABLE: "Unable to reach VNeID signing key service.",
        access_denied: "VNeID sign-in was cancelled or denied.",
        default: "Unable to complete authentication. Please try again.",
      },
    },
  },
  "vi-VN": {
    auth: {
      login: {
        title: "Cổng Thông tin Y tế NeuroScan",
        subtitle: "Đăng nhập bằng tài khoản VNeID định danh mức 2 và Chứng chỉ hành nghề Y.",
        vneidButton: "Đăng nhập bằng VNeID",
        securityNote: "Phiên đăng nhập được bảo vệ bằng cookie HttpOnly trên máy chủ BFF.",
        errorHeading: "Từ chối Xác thực",
      },
      callback: {
        processingTitle: "Đang Xác thực VNeID",
        processingSubtitle: "Đang đối chiếu chứng chỉ hành nghề và cơ sở y tế với Bộ Y tế...",
        errorTitle: "Không thể Hoàn tất Đăng nhập",
        backToLogin: "Quay lại Trang Đăng nhập",
        missingParams: "Thiếu mã ủy quyền OIDC hoặc tham số state.",
      },
      forbidden: {
        title: "Hạn chế Quyền truy cập",
        description: "Vai trò hiện tại của bạn không có quyền truy cập khu vực này.",
      },
      errors: {
        NO_CCHN_PERMISSION: "Tài khoản công dân chưa được cấp Chứng chỉ hành nghề Y (CCHN) hợp lệ.",
        PRACTITIONER_SUSPENDED: "Chứng chỉ hành nghề đang bị đình chỉ hoặc không còn hiệu lực trên hệ thống Bộ Y tế.",
        HOSPITAL_NOT_ONBOARDED: "Cơ sở khám chữa bệnh của bạn chưa được kích hoạt trên hệ thống NeuroScan.",
        USER_SUSPENDED: "Tài khoản hành nghề của bạn đã bị quản trị viên tạm khóa.",
        INVALID_OIDC_STATE: "Phiên đăng nhập đã hết hạn hoặc không khớp. Vui lòng đăng nhập lại.",
        INVALID_OIDC_NONCE: "Xác thực bảo mật OIDC không thành công. Vui lòng thử lại.",
        INVALID_ID_TOKEN: "Không thể xác thực chữ ký danh tính từ VNeID.",
        OIDC_TOKEN_EXCHANGE_FAILED: "Không thể đổi mã ủy quyền VNeID.",
        OIDC_JWKS_UNAVAILABLE: "Không thể kết nối dịch vụ khóa xác thực VNeID.",
        access_denied: "Quá trình đăng nhập VNeID đã bị hủy hoặc từ chối.",
        default: "Không thể hoàn tất xác thực. Vui lòng thử lại.",
      },
    },
  },
});
