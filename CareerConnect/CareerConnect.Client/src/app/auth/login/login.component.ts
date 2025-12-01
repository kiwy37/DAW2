import {
  Component,
  ViewEncapsulation,
  OnInit,
  AfterViewInit,
  OnDestroy
} from '@angular/core';
import { AuthService, RegisterRequest } from '../auth.service';
import { Router } from '@angular/router';

declare const FB: any;
declare const google: any;

type ViewMode = 'login' | 'register' | 'verification' | 'forgotPassword' | 'resetPassword';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
  encapsulation: ViewEncapsulation.None,
})
export class LoginComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly linkedInConfig = {
    clientId: '77qbiu7uucxtzn',
    redirectUri: 'https://localhost:52623/linkedin-callback.html'
  };

  private readonly googleClientId = '937265656787-unp24ld8lqsjbu8jh3rvmjct1i0d66ei.apps.googleusercontent.com';

  currentView: ViewMode = 'login';
  isLoading = false;
  isVerifying = false;

  loginData = { email: '', password: '' };
  registerData = {
    email: '',
    password: '',
    firstname: '',
    lastname: '',
    phone: '',
    birthdate: '',
  };

  forgotPasswordData = { email: '' };
  resetPasswordData = { newPassword: '', confirmPassword: '' };

  codeDigits = ['', '', '', '', '', ''];
  errors: any = {};
  successMessage = '';
  pendingVerification = false;
  currentEmail = '';
  verificationType: 'Login' | 'Register' | 'ResetPassword' = 'Login';
  googleButtonRendered = false;
  linkedInPopup: Window | null = null;
  linkedInCheckInterval: any = null;

  constructor(private authService: AuthService, private router: Router) {}

  ngOnInit(): void {
    this.authService.logout();
    this.preventBackNavigation();
    this.initFacebookSDK();
    this.setupLinkedInMessageListener();
    this.loadGoogleScript();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.renderGoogleButton(), 500);
  }

  ngOnDestroy(): void {
    this.cleanupLinkedInAuth();
  }

  // ==================== VIEW MANAGEMENT ====================

  showView(view: ViewMode): void {
    this.currentView = view;
    this.resetFormState();

    if (view === 'login') {
      setTimeout(() => this.renderGoogleButton(), 100);
    }
  }

  get showLogin(): boolean { return this.currentView === 'login'; }
  get showRegister(): boolean { return this.currentView === 'register'; }
  get showVerification(): boolean { return this.currentView === 'verification'; }
  get showForgotPassword(): boolean { return this.currentView === 'forgotPassword'; }
  get showResetPassword(): boolean { return this.currentView === 'resetPassword'; }

  private resetFormState(): void {
    this.errors = {};
    this.successMessage = '';
    this.codeDigits = ['', '', '', '', '', ''];
  }

  private preventBackNavigation(): void {
    window.history.pushState(null, '', window.location.href);
    window.onpopstate = () => window.history.pushState(null, '', window.location.href);
  }

  // ==================== LINKEDIN AUTHENTICATION ====================

  setupLinkedInMessageListener(): void {
    window.addEventListener('message', (event) => {
      if (event.origin !== window.location.origin) return;

      if (event.data.type === 'LINKEDIN_AUTH_CODE') {
        this.handleLinkedInCode(event.data.code, event.data.state);
      } else if (event.data.type === 'LINKEDIN_AUTH_ERROR') {
        this.handleLinkedInError(event.data.errorDescription);
      }
    }, false);
  }

  async onLinkedInLogin(): Promise<void> {
    try {
      this.errors.general = '';
      this.isLoading = true;

      const state = this.generateRandomState();
      sessionStorage.setItem('linkedin_oauth_state', state);

      const authUrl = this.buildLinkedInAuthUrl(state);
      this.openLinkedInPopup(authUrl);
    } catch (error) {
      console.error('LinkedIn login error:', error);
      this.errors.general = 'Failed to initiate LinkedIn login.';
      this.isLoading = false;
    }
  }

  private buildLinkedInAuthUrl(state: string): string {
    const params = new URLSearchParams({
      response_type: 'code',
      client_id: this.linkedInConfig.clientId,
      redirect_uri: this.linkedInConfig.redirectUri,
      state: state,
      scope: 'openid profile email'
    });

    return `https://www.linkedin.com/oauth/v2/authorization?${params.toString()}`;
  }

  private openLinkedInPopup(url: string): void {
    const width = 600;
    const height = 700;
    const left = (window.screen.width - width) / 2;
    const top = (window.screen.height - height) / 2;

    this.linkedInPopup = window.open(
      url,
      'LinkedIn Login',
      `width=${width},height=${height},top=${top},left=${left},toolbar=no,menubar=no,scrollbars=yes,resizable=yes`
    );

    this.monitorPopupClosure();
  }

  private monitorPopupClosure(): void {
    this.linkedInCheckInterval = setInterval(() => {
      if (this.linkedInPopup?.closed) {
        clearInterval(this.linkedInCheckInterval);
        this.linkedInPopup = null;

        if (this.isLoading) {
          this.isLoading = false;
          this.errors.general = 'LinkedIn authentication was cancelled.';
        }
      }
    }, 500);
  }

  private handleLinkedInCode(code: string, state: string): void {
    try {
      this.cleanupLinkedInAuth();

      if (!this.verifyLinkedInState(state)) {
        this.errors.general = 'Security verification failed. Please try again.';
        this.isLoading = false;
        return;
      }

      sessionStorage.removeItem('linkedin_oauth_state');

      this.authService.linkedInLogin(code).subscribe({
        next: (authResponse) => {
          this.isLoading = false;
          this.handleSuccessfulAuth(authResponse, true);
        },
        error: (err) => {
          this.isLoading = false;
          this.handleLinkedInError(err.error?.error || err.error?.details || 'LinkedIn authentication failed.');
        }
      });
    } catch (error) {
      console.error('Error handling LinkedIn code:', error);
      this.errors.general = 'An error occurred during authentication.';
      this.isLoading = false;
    }
  }

  private verifyLinkedInState(state: string): boolean {
    const savedState = sessionStorage.getItem('linkedin_oauth_state');
    if (state !== savedState) {
      console.error('State mismatch - possible CSRF attack');
      return false;
    }
    return true;
  }

  private handleLinkedInError(errorDescription?: string): void {
    this.errors.general = errorDescription || 'LinkedIn authentication failed.';
    this.isLoading = false;
  }

  private cleanupLinkedInAuth(): void {
    if (this.linkedInCheckInterval) {
      clearInterval(this.linkedInCheckInterval);
    }
    if (this.linkedInPopup && !this.linkedInPopup.closed) {
      this.linkedInPopup.close();
    }
    this.linkedInPopup = null;
  }

  private generateRandomState(): string {
    const array = new Uint32Array(4);
    window.crypto.getRandomValues(array);
    return Array.from(array, (dec) => dec.toString(16).padStart(8, '0')).join('');
  }

  // ==================== FACEBOOK AUTHENTICATION ====================

  initFacebookSDK(): void {
    if (typeof FB !== 'undefined') {
      this.configureFacebookSDK();
      return;
    }

    const script = document.createElement('script');
    script.src = 'https://connect.facebook.net/en_US/sdk.js';
    script.async = true;
    script.defer = true;
    script.onload = () => this.configureFacebookSDK();
    document.body.appendChild(script);
  }

  private configureFacebookSDK(): void {
    FB.init({
      appId: '25212061275130005',
      cookie: true,
      xfbml: true,
      version: 'v18.0',
    });
  }

  onFacebookLogin(): void {
    FB.login((response: any) => {
      if (response.authResponse) {
        this.isLoading = true;

        FB.api('/me', { fields: 'id,first_name,last_name' }, (userInfo: any) => {
          const tempEmail = `facebook_${userInfo.id}@careerconnect.temp`;

          this.authService.facebookLogin(
            response.authResponse.accessToken,
            tempEmail,
            userInfo.first_name,
            userInfo.last_name,
            userInfo.id
          ).subscribe({
            next: (authResponse) => {
              this.isLoading = false;
              this.handleSuccessfulAuth(authResponse, true);
            },
            error: (err) => {
              this.isLoading = false;
              this.errors.general = err.error?.error || err.error?.message || 'Facebook login failed.';
            }
          });
        });
      }
    }, { scope: 'public_profile' });
  }

  // ==================== GOOGLE AUTHENTICATION ====================

  loadGoogleScript(): void {
    if (document.querySelector('script[src="https://accounts.google.com/gsi/client"]')) {
      setTimeout(() => this.renderGoogleButton(), 300);
      return;
    }

    const script = document.createElement('script');
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.defer = true;
    script.onload = () => setTimeout(() => this.initGoogleSDK(), 200);
    script.onerror = () => {
      this.errors.general = 'Failed to load Google Sign-In. Please refresh the page.';
    };
    document.head.appendChild(script);
  }

  initGoogleSDK(): void {
    try {
      if (typeof google === 'undefined' || !google.accounts) {
        setTimeout(() => this.initGoogleSDK(), 500);
        return;
      }

      google.accounts.id.initialize({
        client_id: this.googleClientId,
        callback: (response: any) => this.handleGoogleCallback(response),
        auto_select: false,
        cancel_on_tap_outside: true,
        ux_mode: 'popup',
      });

      this.renderGoogleButton();
    } catch (error) {
      console.error('Error initializing Google SDK:', error);
    }
  }

  renderGoogleButton(): void {
    if (this.googleButtonRendered) return;

    const buttonContainer = document.getElementById('googleButton');
    if (!buttonContainer) {
      setTimeout(() => this.renderGoogleButton(), 200);
      return;
    }

    try {
      if (typeof google === 'undefined' || !google.accounts?.id) {
        setTimeout(() => this.renderGoogleButton(), 300);
        return;
      }

      buttonContainer.innerHTML = '';

      google.accounts.id.renderButton(buttonContainer, {
        theme: 'outline',
        size: 'large',
        type: 'standard',
        text: 'signin_with',
        shape: 'rectangular',
        logo_alignment: 'left',
        width: 280,
      });

      this.googleButtonRendered = true;
    } catch (error) {
      console.error('Error rendering Google button:', error);
      setTimeout(() => this.renderGoogleButton(), 500);
    }
  }

  handleGoogleCallback(response: any): void {
    this.isLoading = true;
    this.errors.general = '';

    this.authService.googleLogin(response.credential).subscribe({
      next: (authResponse) => {
        this.isLoading = false;
        this.handleSuccessfulAuth(authResponse, true);
      },
      error: (err) => {
        this.isLoading = false;
        this.errors.general = err.error?.message || 'Google login failed. Please try again.';
      }
    });
  }

  // ==================== LOGIN ====================

  onLogin(): void {
    this.resetFormState();

    if (!this.validateLoginForm()) return;

    this.isLoading = true;

    this.authService.initiateLogin(this.loginData.email, this.loginData.password).subscribe({
      next: () => {
        this.isLoading = false;
        this.showVerificationView('Login');
      },
      error: (err) => {
        this.isLoading = false;
        this.handleLoginError(err);
      }
    });
  }

  private validateLoginForm(): boolean {
    if (!this.validateEmail(this.loginData.email)) {
      this.errors.loginEmail = 'Please enter a valid email address.';
      return false;
    }

    if (this.loginData.password.length < 6) {
      this.errors.loginPassword = 'Password must be at least 6 characters long.';
      return false;
    }

    return true;
  }

  private handleLoginError(err: any): void {
    const errorMessage = err.status === 0 
      ? 'Cannot connect to server.'
      : err.error?.message || 'An error occurred. Please try again.';
    
    this.errors.loginPassword = errorMessage;
  }

  // ==================== REGISTER ====================

  onRegister(): void {
    this.resetFormState();

    if (!this.validateRegisterForm()) return;

    this.isLoading = true;

    const registerRequest: RegisterRequest = {
      email: this.registerData.email,
      password: this.registerData.password,
      lastName: this.registerData.lastname,
      firstName: this.registerData.firstname,
      phone: this.registerData.phone || undefined,
      birthDate: this.registerData.birthdate,
      roleId: 2,
    };

    this.authService.initiateRegister(registerRequest).subscribe({
      next: () => {
        this.isLoading = false;
        this.showVerificationView('Register');
      },
      error: (err) => {
        this.isLoading = false;
        this.handleRegisterError(err);
      }
    });
  }

  private validateRegisterForm(): boolean {
    let isValid = true;

    if (!this.validateEmail(this.registerData.email)) {
      this.errors.registerEmail = 'Please enter a valid email address.';
      isValid = false;
    }

    if (this.registerData.password.length < 6) {
      this.errors.registerPassword = 'Password must be at least 6 characters long.';
      isValid = false;
    }

    if (!this.registerData.firstname.trim()) {
      this.errors.registerFirstname = 'First name is required.';
      isValid = false;
    }

    if (!this.registerData.lastname.trim()) {
      this.errors.registerLastname = 'Last name is required.';
      isValid = false;
    }

    if (this.registerData.phone && !this.validatePhone(this.registerData.phone)) {
      this.errors.registerPhone = 'Please enter a valid phone number.';
      isValid = false;
    }

    if (!this.validateBirthDate()) {
      isValid = false;
    }

    return isValid;
  }

  private validateBirthDate(): boolean {
    if (!this.registerData.birthdate) {
      this.errors.registerBirthdate = 'Birth date is required.';
      return false;
    }

    const birthDate = new Date(this.registerData.birthdate);
    const today = new Date();
    const age = today.getFullYear() - birthDate.getFullYear();
    const monthDiff = today.getMonth() - birthDate.getMonth();

    const isUnder18 = age < 18 || 
      (age === 18 && monthDiff < 0) || 
      (age === 18 && monthDiff === 0 && today.getDate() < birthDate.getDate());

    if (isUnder18) {
      this.errors.registerBirthdate = 'You must be at least 18.';
      return false;
    }

    return true;
  }

  private handleRegisterError(err: any): void {
    if (err.error?.message) {
      this.errors.general = err.error.message;
    } else if (err.error?.errors) {
      Object.keys(err.error.errors).forEach((key) => {
        const errorKey = `register${key.charAt(0).toUpperCase() + key.slice(1)}`;
        this.errors[errorKey] = err.error.errors[key][0];
      });
    } else {
      this.errors.general = 'An error occurred. Please try again.';
    }
  }

  // ==================== FORGOT PASSWORD ====================

  onForgotPassword(): void {
    this.resetFormState();

    if (!this.validateEmail(this.forgotPasswordData.email)) {
      this.errors.forgotEmail = 'Please enter a valid email address.';
      return;
    }

    this.isLoading = true;

    this.authService.initiateForgotPassword(this.forgotPasswordData.email).subscribe({
      next: () => {
        this.isLoading = false;
        this.currentEmail = this.forgotPasswordData.email;
        this.verificationType = 'ResetPassword';
        this.showView('verification');
        this.successMessage = 'Verification code sent to your email!';
      },
      error: (err) => {
        this.isLoading = false;
        this.errors.forgotEmail = err.error?.message || 'An error occurred. Please try again.';
      }
    });
  }

  // ==================== VERIFICATION ====================

  verifyCode(): void {
    const code = this.codeDigits.join('');

    if (code.length !== 6) {
      this.errors.verification = 'Please enter the complete 6-digit code.';
      return;
    }

    this.isVerifying = true;
    this.errors.verification = '';

    if (this.verificationType === 'ResetPassword') {
      this.verifyResetCode(code);
    } else if (this.verificationType === 'Login') {
      this.verifyLoginCode(code);
    } else {
      this.verifyRegisterCode(code);
    }
  }

  private verifyResetCode(code: string): void {
    this.authService.verifyResetCode(this.currentEmail, code).subscribe({
      next: () => {
        this.isVerifying = false;
        this.showView('resetPassword');
        this.successMessage = 'Code verified! Please enter your new password.';
      },
      error: (err) => {
        this.isVerifying = false;
        this.handleVerificationError(err);
      }
    });
  }

  private verifyLoginCode(code: string): void {
    this.authService.completeLogin(this.currentEmail, code).subscribe({
      next: () => {
        this.isVerifying = false;
        this.router.navigate(['/landing']);
      },
      error: (err) => {
        this.isVerifying = false;
        this.handleVerificationError(err);
      }
    });
  }

  private verifyRegisterCode(code: string): void {
    const registerRequest: RegisterRequest = {
      email: this.currentEmail,
      password: this.registerData.password,
      lastName: this.registerData.lastname,
      firstName: this.registerData.firstname,
      phone: this.registerData.phone || undefined,
      birthDate: this.registerData.birthdate,
      roleId: 2,
    };

    this.authService.finalizeRegister(registerRequest, code).subscribe({
      next: () => {
        this.isVerifying = false;
        this.router.navigate(['/landing']);
      },
      error: (err) => {
        this.isVerifying = false;
        this.handleVerificationError(err);
      }
    });
  }

  private showVerificationView(type: 'Login' | 'Register'): void {
    this.pendingVerification = true;
    this.currentEmail = type === 'Login' ? this.loginData.email : this.registerData.email;
    this.verificationType = type;
    this.showView('verification');
    this.successMessage = 'Verification code sent to your email!';
  }

  private handleVerificationError(err: any): void {
    const errorMessage = err.status === 0
      ? 'Cannot connect to server.'
      : err.error?.message || 'Invalid verification code. Please try again.';
    
    this.errors.verification = errorMessage;
  }

  resendCode(): void {
    this.errors.verification = '';

    this.authService.resendVerificationCode(this.currentEmail, this.verificationType).subscribe({
      next: () => {
        this.successMessage = 'Verification code resent successfully!';
      },
      error: (err) => {
        this.errors.verification = 'Failed to resend code. Please try again.';
      }
    });
  }

  backToLogin(): void {
    this.showView('login');
    this.pendingVerification = false;
  }

  // ==================== RESET PASSWORD ====================

  onResetPassword(): void {
    this.errors = {};

    if (this.resetPasswordData.newPassword.length < 6) {
      this.errors.newPassword = 'Password must be at least 6 characters long.';
      return;
    }

    if (this.resetPasswordData.newPassword !== this.resetPasswordData.confirmPassword) {
      this.errors.confirmPassword = 'Passwords do not match.';
      return;
    }

    this.isLoading = true;
    const code = this.codeDigits.join('');

    this.authService.resetPassword(this.currentEmail, code, this.resetPasswordData.newPassword).subscribe({
      next: () => {
        this.isLoading = false;
        this.successMessage = 'Password reset successfully! Redirecting to login...';

        setTimeout(() => {
          this.showView('login');
          this.resetPasswordData = { newPassword: '', confirmPassword: '' };
          this.codeDigits = ['', '', '', '', '', ''];
        }, 2000);
      },
      error: (err) => {
        this.isLoading = false;
        this.errors.general = err.error?.message || 'Failed to reset password. Please try again.';
      }
    });
  }

  // ==================== HELPER METHODS ====================

  private handleSuccessfulAuth(response: any, isSocialLogin: boolean = false): void {
    this.successMessage = 'Authentication successful! Redirecting...';
    setTimeout(() => this.router.navigate(['/landing']), 1500);
  }

  onCodeInput(index: number, event: any): void {
    const value = event.target.value;

    if (!/^\d*$/.test(value)) {
      event.target.value = '';
      this.codeDigits[index] = '';
      return;
    }

    if (value.length === 1) {
      this.codeDigits[index] = value;
      if (index < 5) {
        const inputs = document.querySelectorAll('.code-input');
        (inputs[index + 1] as HTMLElement).focus();
      }
    }
  }

  onCodeKeydown(index: number, event: any): void {
    if (event.key === 'Backspace' && !event.target.value && index > 0) {
      const inputs = document.querySelectorAll('.code-input');
      (inputs[index - 1] as HTMLElement).focus();
    }
  }

  private validateEmail(email: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  }

  private validatePhone(phone: string): boolean {
    return /^[0-9+\-\s()]{10,}$/.test(phone);
  }
}