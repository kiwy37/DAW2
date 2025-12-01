import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject, tap } from 'rxjs';

// ==================== INTERFACES ====================

export interface AuthResponse {
  token: string;
  user: {
    id: number;
    email: string;
    lastName: string;
    firstName: string;
    phone?: string;
    birthDate: string;
    roleName: string;
    createdAt: string;
  };
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  lastName: string;
  firstName: string;
  phone?: string;
  birthDate: string;
  roleId: number;
}

export interface VerifyCodeDto {
  email: string;
  code: string;
  verificationType: string;
}

export interface CreateUserWithCodeDto extends RegisterRequest {
  code: string;
}

export interface SocialLoginDto {
  provider: 'Google' | 'Facebook' | 'Twitter' | 'LinkedIn';
  accessToken: string;
  email?: string;
  firstName?: string;
  lastName?: string;
  providerId?: string;
}

export interface LinkedInLoginDto {
  code: string;
}

export interface ForgotPasswordDto {
  email: string;
}

export interface VerifyResetCodeDto {
  email: string;
  code: string;
}

export interface ResetPasswordDto {
  email: string;
  code: string;
  newPassword: string;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly apiUrl = '/api/auth';
  private currentUserSubject = new BehaviorSubject<any>(null);
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {
    this.initializeUserFromStorage();
  }

  // ==================== INITIALIZATION ====================

  private initializeUserFromStorage(): void {
    const token = this.getToken();
    if (token) {
      const userData = localStorage.getItem('currentUser');
      if (userData) {
        this.currentUserSubject.next(JSON.parse(userData));
      }
    }
  }

  // ==================== LOGIN FLOW ====================

  initiateLogin(email: string, password: string): Observable<any> {
    const request: LoginRequest = { email, password };
    return this.http.post(`${this.apiUrl}/login/initiate`, request);
  }

  completeLogin(email: string, code: string): Observable<AuthResponse> {
    const request: VerifyCodeDto = { 
      email, 
      code, 
      verificationType: 'Login' 
    };
    return this.http.post<AuthResponse>(`${this.apiUrl}/login/complete`, request).pipe(
      tap(response => this.handleAuthSuccess(response))
    );
  }

  // ==================== REGISTER FLOW ====================

  initiateRegister(data: RegisterRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/register/initiate`, data);
  }

  finalizeRegister(data: RegisterRequest, code: string): Observable<AuthResponse> {
    const request: CreateUserWithCodeDto = { ...data, code };
    return this.http.post<AuthResponse>(`${this.apiUrl}/register/finalize`, request).pipe(
      tap(response => this.handleAuthSuccess(response))
    );
  }

  // ==================== FORGOT PASSWORD FLOW ====================

  initiateForgotPassword(email: string): Observable<any> {
    const request: ForgotPasswordDto = { email };
    return this.http.post(`${this.apiUrl}/forgot-password`, request);
  }

  verifyResetCode(email: string, code: string): Observable<any> {
    const request: VerifyResetCodeDto = { email, code };
    return this.http.post(`${this.apiUrl}/verify-reset-code`, request);
  }

  resetPassword(email: string, code: string, newPassword: string): Observable<any> {
    const request: ResetPasswordDto = { email, code, newPassword };
    return this.http.post(`${this.apiUrl}/reset-password`, request);
  }

  // ==================== CODE MANAGEMENT ====================

  resendVerificationCode(email: string, type: string): Observable<any> {
    const request = { email, verificationType: type };
    return this.http.post(`${this.apiUrl}/resend-code`, request);
  }

  // ==================== SOCIAL LOGIN ====================

  googleLogin(idToken: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.apiUrl}/google-login`, { idToken }).pipe(
      tap(response => this.handleAuthSuccess(response))
    );
  }

  facebookLogin(
    accessToken: string, 
    email: string, 
    firstName: string, 
    lastName: string, 
    facebookId: string
  ): Observable<AuthResponse> {
    const request: SocialLoginDto = {
      provider: 'Facebook',
      accessToken,
      email,
      firstName,
      lastName,
      providerId: facebookId
    };
    return this.http.post<AuthResponse>(`${this.apiUrl}/social-login`, request).pipe(
      tap(response => this.handleAuthSuccess(response))
    );
  }

  twitterLogin(
    accessToken: string, 
    email: string, 
    firstName: string, 
    lastName: string, 
    twitterId: string
  ): Observable<AuthResponse> {
    const request: SocialLoginDto = {
      provider: 'Twitter',
      accessToken,
      email,
      firstName,
      lastName,
      providerId: twitterId
    };
    return this.http.post<AuthResponse>(`${this.apiUrl}/social-login`, request).pipe(
      tap(response => this.handleAuthSuccess(response))
    );
  }

  linkedInLogin(code: string): Observable<AuthResponse> {
    const request: LinkedInLoginDto = { code };
    return this.http.post<AuthResponse>(`${this.apiUrl}/linkedin-login`, request).pipe(
      tap(response => this.handleAuthSuccess(response))
    );
  }

  // ==================== AUTHENTICATION STATE ====================

  logout(): void {
    localStorage.removeItem('token');
    localStorage.removeItem('currentUser');
    this.currentUserSubject.next(null);
  }

  getToken(): string | null {
    return localStorage.getItem('token');
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) return false;
    
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.exp * 1000 > Date.now();
    } catch {
      return false;
    }
  }

  getCurrentUser(): any {
    return this.currentUserSubject.value;
  }

  // ==================== PRIVATE HELPERS ====================

  private handleAuthSuccess(response: AuthResponse): void {
    this.setToken(response.token);
    this.setCurrentUser(response.user);
  }

  private setToken(token: string): void {
    localStorage.setItem('token', token);
  }

  private setCurrentUser(user: any): void {
    localStorage.setItem('currentUser', JSON.stringify(user));
    this.currentUserSubject.next(user);
  }
}