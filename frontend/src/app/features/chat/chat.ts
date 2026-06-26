import { Component, signal, computed, OnInit, OnDestroy, inject, viewChild, ElementRef, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { ConversationService, ConversationDto, MessageDto, ParticipantDto } from '../../core/services/conversation.service';
import { AuthService } from '../../core/services/auth.service';
import { UserService } from '../../core/services/user.service';
import { ClassService } from '../../core/services/class.service';
import { SubjectService } from '../../core/services/subject.service';
import { AcademicYearService } from '../../core/services/academic-year.service';
import { RoleService } from '../../shared/role.service';
import { getBackendBaseUrl } from '../../core/utils/api-url';

interface ChatMessage {
  id: number;
  senderId: number;
  senderName: string;
  senderRole: string;
  content: string;
  sentAt: string;
  isMine: boolean;
  isEdited: boolean;
  attachmentUrl?: string;
  attachmentType?: string;
  voiceText?: string;
}

interface ChatConversation {
  id: number;
  name: string;
  type: 'individual' | 'group';
  lastMessage: string;
  lastTime: string;
  unread: boolean;
  participants: ParticipantDto[];
  messages: ChatMessage[];
}

@Component({
  selector: 'app-chat',
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './chat.html',
  styleUrl: './chat.css',
})
export class Chat implements OnInit, OnDestroy {
  conversationService = inject(ConversationService);
  authService = inject(AuthService);
  private userService = inject(UserService);
  private classService = inject(ClassService);
  private subjectService = inject(SubjectService);
  private academicYearService = inject(AcademicYearService);
  roleService = inject(RoleService);
  private cdr = inject(ChangeDetectorRef);

  sidebarOpen = signal(false);
  showConvList = signal(true);
  activeFilter = signal<'all' | 'groups' | 'individual'>('all');
  activeConversationId = signal<number | null>(null);
  newMessage = signal('');
  searchTerm = signal('');
  loadingConversations = signal(false);
  loadingMessages = signal(false);
  loadingOlder = signal(false);
  hasMoreMessages = signal(true);
  messagesPage = signal(1);
  pageSize = 20;
  totalUnread = signal(0);

  conversations = signal<ChatConversation[]>([]);

  activeConversation = computed(() =>
    this.conversations().find(c => c.id === this.activeConversationId()) ?? null
  );
  isBlocked = signal(false);
  blockedByOther = signal(false);
  blockedByMe = signal<Set<number>>(new Set());

  filteredConversations = computed(() => {
    const filter = this.activeFilter();
    const term = this.searchTerm().toLowerCase();
    let list = this.conversations();
    if (filter === 'groups') list = list.filter(c => c.type === 'group');
    else if (filter === 'individual') list = list.filter(c => c.type === 'individual');
    if (term) list = list.filter(c => c.name.toLowerCase().includes(term));
    return list;
  });

  showProfilePanel = signal(false);
  showCreateGroupModal = signal(false);
  groupType = signal<'all-subjects' | 'specific-subject'>('all-subjects');
  groupTitle = signal('');
  selectedSubjectId = signal<number | null>(null);
  selectedClassId = signal<number | null>(null);
  selectedAcademicYearId = signal<number | null>(null);
  creatingGroup = signal(false);
  classList = signal<any[]>([]);
  subjectList = signal<any[]>([]);
  academicYearList = signal<any[]>([]);

  showUserSearch = signal(false);
  userSearchQuery = signal('');
  userSearchResults = signal<any[]>([]);
  userSearchTimeout: any = null;

  showAddParticipantModal = signal(false);
  showGroupMembersModal = signal(false);
  searchUserQuery = signal('');
  searchUserResults = signal<any[]>([]);
  addingParticipant = signal(false);
  removingParticipantId = signal<number | null>(null);
  searchTimeout: any = null;

  typingTimeout: any = null;
  private msgSub?: Subscription;
  private updateSub?: Subscription;
  private deleteSub?: Subscription;
  editingMessageId = signal<number | null>(null);
  editingMessageContent = signal('');
  errorMessage = signal<string | null>(null);
  selectedFile = signal<File | null>(null);
  uploadingFile = signal(false);

  playingAudioId = signal<number | null>(null);
  audioProgressMap = new Map<number, number>();
  currentAudio: HTMLAudioElement | null = null;
  showTranscriptFor = signal<Set<number>>(new Set());

  messagesContainer = viewChild<ElementRef>('messagesContainer');

  private scrollToBottom(): void {
    const doScroll = () =>
      this.messagesContainer()?.nativeElement?.scrollTo({ top: 99999, behavior: 'instant' });
    setTimeout(doScroll, 50);
    setTimeout(doScroll, 300);
  }

  toggleTranscript(msgId: number): void {
    const currentSet = this.showTranscriptFor();
    const newSet = new Set(currentSet);
    
    if (newSet.has(msgId)) { 
      console.log('Hiding transcript for message:', msgId);
      newSet.delete(msgId); 
    } else { 
      console.log('Showing transcript for message:', msgId);
      newSet.add(msgId); 
    }
    
    this.showTranscriptFor.set(newSet);
    console.log('Current transcript visibility set:', Array.from(newSet));
  }

  // Auto-show transcript for sent voice messages with text
  private autoShowTranscriptForSent(msgId: number, isMine: boolean, hasVoiceText: boolean): void {
    if (isMine && hasVoiceText) {
      // Automatically show transcript for messages sent by me that have voiceText
      const set = new Set(this.showTranscriptFor());
      set.add(msgId);
      this.showTranscriptFor.set(set);
      console.log('Auto-showing transcript for sent message:', msgId);
    }
  }

  transcribingMsgId = signal<number | null>(null);

  requestTranscription(msg: any): void {
    // Check if already transcribing
    if (this.transcribingMsgId() !== null) {
      console.log('Already transcribing another message');
      return;
    }
    
    const convId = this.activeConversationId();
    if (!convId) return;

    this.transcribingMsgId.set(msg.id);
    console.log('Starting transcription for message:', msg.id);

    // Get the audio file URL
    const audioUrl = msg.attachmentUrl || msg.content;
    
    // Option 1: If backend supports transcription API
    // Just send the audio URL to backend for transcription
    this.conversationService.transcribeMessage(convId, msg.id, '').subscribe({
      next: res => {
        console.log('Transcription API response:', res);
        if (res.isSuccess && res.data && res.data.voiceText) {
          // Backend returned transcription
          this.conversations.update(list => list.map(c => {
            if (c.id !== convId) return c;
            return {
              ...c,
              messages: c.messages.map(m => m.id === msg.id
                ? { ...m, voiceText: res.data!.voiceText }
                : m),
            };
          }));
          this.showTranscriptFor.set(new Set([...this.showTranscriptFor(), msg.id]));
          this.transcribingMsgId.set(null);
        } else {
          // Backend doesn't support auto-transcription, use browser recognition
          console.log('Backend does not support auto-transcription, using browser...');
          this.transcribeUsingBrowser(msg, convId);
        }
      },
      error: (err) => {
        console.error('Transcription API error:', err);
        // Fallback to browser recognition
        console.log('API failed, falling back to browser recognition...');
        this.transcribeUsingBrowser(msg, convId);
      }
    });
  }

  private transcribeUsingBrowser(msg: any, convId: number): void {
    const SpeechRecognition = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (!SpeechRecognition) {
      this.showError('المتصفح لا يدعم التعرف على الصوت');
      this.transcribingMsgId.set(null);
      return;
    }

    const recognition = new SpeechRecognition();
    recognition.lang = 'ar-EG';
    recognition.interimResults = true;
    recognition.continuous = true;
    recognition.maxAlternatives = 1;

    let finalTranscript = '';
    let recognitionStarted = false;
    let audioEnded = false;

    recognition.onstart = () => {
      recognitionStarted = true;
      console.log('Speech recognition started');
    };

    recognition.onresult = (event: any) => {
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const transcript = event.results[i][0].transcript;
        if (event.results[i].isFinal) {
          finalTranscript += transcript + ' ';
        }
      }
    };

    recognition.onerror = (event: any) => {
      console.error('Speech recognition error:', event.error);
      recognitionStarted = false;
      try { recognition.stop(); } catch {}
      
      if (event.error !== 'aborted' && event.error !== 'no-speech') {
        this.showError('حدث خطأ في التعرف على الصوت');
      }
      this.transcribingMsgId.set(null);
    };

    recognition.onend = () => {
      recognitionStarted = false;
      if (audioEnded) {
        this.processTranscription(convId, msg.id, finalTranscript);
      }
    };

    // Play audio with VERY LOW volume (almost silent but still detectable by recognition)
    const audio = new Audio(this.uploadUrl(msg.attachmentUrl || msg.content));
    audio.volume = 0.01; // 1% volume - تقريباً ما يتسمعش بس الـ Recognition يقدر يسمعه
    
    audio.onended = () => {
      audioEnded = true;
      setTimeout(() => {
        if (recognitionStarted) {
          try { recognition.stop(); } catch (e) {
            this.processTranscription(convId, msg.id, finalTranscript);
          }
        } else {
          this.processTranscription(convId, msg.id, finalTranscript);
        }
      }, 500);
    };

    audio.onerror = () => {
      if (recognitionStarted) {
        try { recognition.stop(); } catch {}
      }
      this.transcribingMsgId.set(null);
      this.showError('حدث خطأ في تحميل التسجيل الصوتي');
    };

    audio.play().then(() => {
      setTimeout(() => {
        try { 
          recognition.start();
        } catch (e) { 
          this.transcribingMsgId.set(null);
          this.showError('فشل بدء التعرف على الصوت');
        }
      }, 100);
    }).catch(() => {
      this.transcribingMsgId.set(null);
      this.showError('فشل تحميل التسجيل الصوتي');
    });
  }

  private processTranscription(convId: number, msgId: number, transcript: string): void {
    const text = transcript.trim();
    console.log('Processing transcription:', text);
    
    if (text && text.length > 0) {
      console.log('Sending transcription to API...');
      this.conversationService.transcribeMessage(convId, msgId, text).subscribe({
        next: res => {
          console.log('Transcription save response:', res);
          if (res.isSuccess) {
            this.conversations.update(list => list.map(c => {
              if (c.id !== convId) return c;
              return {
                ...c,
                messages: c.messages.map(m => m.id === msgId
                  ? { ...m, voiceText: res.data?.voiceText ?? text }
                  : m),
              };
            }));
            this.showTranscriptFor.set(new Set([...this.showTranscriptFor(), msgId]));
            this.transcribingMsgId.set(null);
          } else {
            this.showError(res.message || 'حدث خطأ في حفظ النص');
            this.transcribingMsgId.set(null);
          }
        },
        error: (err) => {
          console.error('Transcription save error:', err);
          this.showError('حدث خطأ في حفظ النص');
          this.transcribingMsgId.set(null);
        }
      });
    } else {
      console.log('No text recognized');
      this.transcribingMsgId.set(null);
      this.showError('لم يتم التعرف على أي نص من التسجيل. جرب تشغيل الرسالة أولاً.');
    }
  }

  toggleVoicePlay(msg: any, url: string): void {
    // If already playing this message, pause it
    if (this.playingAudioId() === msg.id) {
      if (this.currentAudio) {
        this.currentAudio.pause();
        this.currentAudio = null;
      }
      this.playingAudioId.set(null);
      return;
    }
    
    // Stop any currently playing audio
    if (this.currentAudio) {
      this.currentAudio.pause();
      this.currentAudio = null;
    }
    
    // Create new audio instance
    const audio = new Audio(url);
    audio.preload = 'metadata';
    
    const updateProgress = () => {
      if (audio.duration && !isNaN(audio.duration)) {
        const progress = (audio.currentTime / audio.duration) * 100;
        this.audioProgressMap.set(msg.id, progress);
      }
    };
    
    audio.onloadedmetadata = () => {
      this.audioProgressMap.set(msg.id, 0);
    };
    
    audio.onplay = () => {
      this.playingAudioId.set(msg.id);
      updateProgress();
    };
    
    audio.ontimeupdate = () => {
      updateProgress();
    };
    
    audio.onended = () => {
      this.playingAudioId.set(null);
      this.audioProgressMap.set(msg.id, 0);
      this.currentAudio = null;
    };
    
    audio.onerror = (e) => {
      console.error('Audio playback error:', e);
      this.playingAudioId.set(null);
      this.audioProgressMap.set(msg.id, 0);
      this.currentAudio = null;
      this.showError('حدث خطأ في تشغيل التسجيل الصوتي');
    };
    
    audio.play().catch((error) => {
      console.error('Failed to play audio:', error);
      this.showError('فشل تشغيل التسجيل الصوتي');
      this.playingAudioId.set(null);
    });
    
    this.currentAudio = audio;
  }

  isRecording = signal(false);
  recordingTimer = signal('00:00');
  liveTranscript = signal('');
  pendingVoice = signal<{ blob: Blob; url: string; transcript: string } | null>(null);
  recordInterval: any = null;
  mediaRecorder: MediaRecorder | null = null;
  private mediaStream: MediaStream | null = null;
  speechRecognition: any = null;
  audioChunks: Blob[] = [];
  pendingVoiceTimeout: any = null;

  startVoiceRecording(): void {
    this.discardPendingVoice();
    this.audioChunks = [];
    this.liveTranscript.set('');
    this.recordingTimer.set('00:00');
    
    // Check if browser supports required APIs
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      this.showError('المتصفح لا يدعم التسجيل الصوتي');
      return;
    }
    
    navigator.mediaDevices.getUserMedia({ 
      audio: {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
        sampleRate: 44100
      } 
    }).then(stream => {
      this.mediaStream = stream;
      // Determine best audio format supported by browser
      let mimeType = 'audio/webm';
      if (MediaRecorder.isTypeSupported('audio/webm;codecs=opus')) {
        mimeType = 'audio/webm;codecs=opus';
      } else if (MediaRecorder.isTypeSupported('audio/ogg;codecs=opus')) {
        mimeType = 'audio/ogg;codecs=opus';
      } else if (MediaRecorder.isTypeSupported('audio/mp4')) {
        mimeType = 'audio/mp4';
      }
      
      try {
        this.mediaRecorder = new MediaRecorder(stream, { 
          mimeType,
          audioBitsPerSecond: 128000
        });
      } catch (e) {
        // Fallback without options if browser doesn't support them
        this.mediaRecorder = new MediaRecorder(stream);
      }
      
      this.mediaRecorder.ondataavailable = e => { 
        if (e.data.size > 0) {
          this.audioChunks.push(e.data); 
        }
      };
      
      this.mediaRecorder.onstop = () => {
        // Stop all tracks
        stream.getTracks().forEach(t => t.stop());
        
        // Clear timer
        if (this.recordInterval) {
          clearInterval(this.recordInterval);
          this.recordInterval = null;
        }
        
        this.isRecording.set(false);
        
        if (this.audioChunks.length === 0) {
          this.showError('لم يتم تسجيل أي صوت');
          return;
        }
        
        const actualMimeType = this.mediaRecorder?.mimeType || mimeType;
        const blob = new Blob(this.audioChunks, { type: actualMimeType });
        const url = URL.createObjectURL(blob);
        this.audioChunks = [];
        
        // Stop speech recognition and get final transcript
        const finalTranscript = this.liveTranscript().trim();
        if (this.speechRecognition) {
          try { 
            this.speechRecognition.continuous = false;
            this.speechRecognition.abort(); 
          } catch {}
          this.speechRecognition = null;
        }
        
        // Wait a bit for any final speech results
        this.pendingVoiceTimeout = setTimeout(() => {
          this.pendingVoiceTimeout = null;
          this.liveTranscript.set('');
          this.pendingVoice.set({ blob, url, transcript: finalTranscript });
        }, 800);
      };
      
      this.mediaRecorder.onerror = (event: any) => {
        console.error('MediaRecorder error:', event);
        this.showError('حدث خطأ أثناء التسجيل');
        this.cancelVoiceRecording();
      };
      
      this.mediaRecorder.start(100); // Collect data every 100ms for smoother experience
      this.isRecording.set(true);
      this.startLiveTranscription();
      
      // Start timer
      let sec = 0;
      this.recordInterval = setInterval(() => {
        sec++;
        const m = String(Math.floor(sec / 60)).padStart(2, '0');
        const s = String(sec % 60).padStart(2, '0');
        this.recordingTimer.set(`${m}:${s}`);
      }, 1000);
      
    }).catch((error) => {
      console.error('Microphone access error:', error);
      let errorMsg = 'لا يمكن الوصول إلى الميكروفون';
      if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
        errorMsg = 'تم رفض إذن الوصول للميكروفون. يرجى السماح بالوصول للميكروفون.';
      } else if (error.name === 'NotFoundError' || error.name === 'DevicesNotFoundError') {
        errorMsg = 'لم يتم العثور على ميكروفون متصل بالجهاز';
      }
      this.showError(errorMsg);
    });
  }

  private startLiveTranscription(): void {
    const SpeechRecognition = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (!SpeechRecognition) { 
      this.liveTranscript.set(''); 
      return; 
    }
    
    this.speechRecognition = new SpeechRecognition();
    this.speechRecognition.lang = 'ar-EG';
    this.speechRecognition.interimResults = true;
    this.speechRecognition.continuous = true;
    this.speechRecognition.maxAlternatives = 1;
    
    let fullTranscript = '';
    
    this.speechRecognition.onresult = (e: any) => {
      let interimText = '';
      for (let i = e.resultIndex; i < e.results.length; i++) {
        const transcript = e.results[i][0].transcript;
        if (e.results[i].isFinal) {
          fullTranscript += transcript + ' ';
        } else {
          interimText += transcript;
        }
      }
      this.liveTranscript.set(fullTranscript + interimText);
    };
    
    this.speechRecognition.onerror = (event: any) => {
      console.error('Speech recognition error during recording:', event.error);
      if (event.error === 'no-speech') {
        // Continue silently - user might not be speaking yet
        return;
      }
      if (event.error !== 'aborted') {
        // Try to restart
        try {
          this.speechRecognition?.stop();
          setTimeout(() => {
            if (this.isRecording() && this.speechRecognition) {
              try { this.speechRecognition.start(); } catch {}
            }
          }, 100);
        } catch {}
      }
    };
    
    this.speechRecognition.onend = () => {
      // Auto-restart if still recording
      if (this.isRecording() && this.speechRecognition) {
        try {
          this.speechRecognition.start();
        } catch (e) {
          console.error('Failed to restart speech recognition:', e);
        }
      }
    };
    
    try { 
      this.speechRecognition.start(); 
    } catch (e) {
      console.error('Failed to start speech recognition:', e);
      this.liveTranscript.set('');
    }
  }

  stopVoiceRecording(): void {
    if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
      this.mediaRecorder.stop();
    }
    if (this.speechRecognition) {
      try { 
        this.speechRecognition.continuous = false;
        this.speechRecognition.stop(); 
      } catch {}
    }
  }

  cancelVoiceRecording(): void {
    // Stop speech recognition first
    if (this.speechRecognition) {
      try { 
        this.speechRecognition.continuous = false;
        this.speechRecognition.abort(); 
      } catch {}
      this.speechRecognition = null;
    }
    
    // Stop media recorder
    if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
      this.mediaRecorder.onstop = null;
      this.mediaRecorder.stop();
      this.mediaRecorder = null;
    }

    // Release microphone stream
    if (this.mediaStream) {
      this.mediaStream.getTracks().forEach(t => t.stop());
      this.mediaStream = null;
    }
    
    // Clear timer
    if (this.recordInterval) {
      clearInterval(this.recordInterval);
      this.recordInterval = null;
    }
    
    // Clear pending timeout
    if (this.pendingVoiceTimeout) {
      clearTimeout(this.pendingVoiceTimeout);
      this.pendingVoiceTimeout = null;
    }
    
    // Reset state
    this.isRecording.set(false);
    this.audioChunks = [];
    this.liveTranscript.set('');
    this.recordingTimer.set('00:00');
  }

  discardPendingVoice(): void {
    if (this.pendingVoiceTimeout) {
      clearTimeout(this.pendingVoiceTimeout);
      this.pendingVoiceTimeout = null;
    }
    const pending = this.pendingVoice();
    if (pending) {
      URL.revokeObjectURL(pending.url);
    }
    this.pendingVoice.set(null);
  }

  sendPendingVoice(): void {
    const pending = this.pendingVoice();
    if (!pending) return;
    
    const convId = this.activeConversationId();
    if (!convId) {
      this.discardPendingVoice();
      return;
    }
    
    // Keep the transcript before clearing pending voice
    const transcript = pending.transcript;
    const blob = pending.blob;
    const blobUrl = pending.url;
    
    // Clear pending voice immediately to prevent double send
    this.pendingVoice.set(null);
    
    // Determine file extension based on blob type
    const fileExt = blob.type.includes('webm') ? 'webm' : blob.type.includes('ogg') ? 'ogg' : 'wav';
    const fileName = `voice_${Date.now()}.${fileExt}`;
    const file = new File([blob], fileName, { type: blob.type });
    
    this.uploadingFile.set(true);
    
    this.conversationService.uploadFile(file).subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          // IMPORTANT: Send both the content (for display) and voiceText (for transcript storage)
          // If we have transcript, use it as content, otherwise use a default message
          const messageContent = transcript && transcript.trim() 
            ? transcript.trim() 
            : 'تسجيل صوتي';
          
          console.log('Sending voice message with transcript:', transcript);
          
          // Send message with attachment URL, type, AND voiceText
          this.conversationService.sendMessageRest(
            convId, 
            messageContent,           // The text content (will show if no attachment or as caption)
            res.data.url,             // The uploaded file URL
            blob.type,                // MIME type
            transcript || undefined   // The transcript to save in voiceText field
          ).subscribe({
            next: msgRes => {
              if (msgRes.isSuccess && msgRes.data) {
                console.log('Voice message sent successfully:', msgRes.data);
                this.appendMessage(convId, this.toChatMessage(msgRes.data, this.activeConversation()?.participants));
              } else {
                this.showError(msgRes.message || 'حدث خطأ أثناء الإرسال');
              }
            },
            error: (err) => {
              console.error('Send voice message error:', err);
              this.showError('حدث خطأ أثناء إرسال التسجيل');
            },
            complete: () => {
              this.uploadingFile.set(false);
              // Clean up the blob URL
              URL.revokeObjectURL(blobUrl);
            },
          });
        } else {
          this.uploadingFile.set(false);
          this.showError(res.message || 'حدث خطأ أثناء رفع التسجيل');
          URL.revokeObjectURL(blobUrl);
        }
      },
      error: (err) => {
        console.error('Upload voice file error:', err);
        this.showError('حدث خطأ أثناء رفع التسجيل');
        this.uploadingFile.set(false);
        URL.revokeObjectURL(blobUrl);
      },
    });
  }

  isVoiceMessage(msg: ChatMessage): boolean {
    return !!msg.attachmentUrl && (msg.attachmentType?.startsWith('audio/') ?? false);
  }

  ngOnInit(): void {
    this.loadConversations();
    this.conversationService.startConnection();
    this.loadUnreadCount();

    this.msgSub = this.conversationService.receivedMessage$.subscribe(msg => {
      const activeId = this.activeConversationId();
      this.conversations.update(list => list.map(c => {
        if (c.id !== msg.conversationId) return c;
        if (c.messages.some(m => m.id === msg.id)) return c;
        return {
          ...c,
          messages: c.id === activeId ? [...c.messages, this.toChatMessage(msg, c.participants)] : c.messages,
          lastMessage: msg.content,
          lastTime: this.formatTime(msg.sentAt),
          unread: c.id !== activeId,
        };
      }));
      if (activeId === msg.conversationId) this.scrollToBottom();
    });

    this.updateSub = this.conversationService.updatedMessage$.subscribe(msg => {
      this.conversations.update(list => list.map(c => {
        if (c.id !== msg.conversationId) return c;
        return {
          ...c,
          messages: c.messages.map(m => m.id === msg.id
            ? { ...m, content: msg.content, isEdited: msg.isEdited ?? false, voiceText: msg.voiceText }
            : m),
        };
      }));
    });

    this.deleteSub = this.conversationService.deletedMessage$.subscribe(({ conversationId, messageId }) => {
      this.conversations.update(list => list.map(c => {
        if (c.id !== conversationId) return c;
        return { ...c, messages: c.messages.filter(m => m.id !== messageId) };
      }));
    });
  }

  ngOnDestroy(): void {
    // Unsubscribe from observables
    this.msgSub?.unsubscribe();
    this.updateSub?.unsubscribe();
    this.deleteSub?.unsubscribe();
    
    // Stop SignalR connection
    this.conversationService.stopConnection();
    
    // Clean up audio
    if (this.currentAudio) {
      this.currentAudio.pause();
      this.currentAudio = null;
    }
    
    // Clean up voice recording
    this.cancelVoiceRecording();
    this.discardPendingVoice();
    
    // Clear timeouts
    if (this.typingTimeout) {
      clearTimeout(this.typingTimeout);
      this.typingTimeout = null;
    }
    if (this.userSearchTimeout) {
      clearTimeout(this.userSearchTimeout);
      this.userSearchTimeout = null;
    }
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
      this.searchTimeout = null;
    }
    
    // Clear audio progress map
    this.audioProgressMap.clear();

    // Clean up scroll listener
    this.scrollCleanup?.();
  }

  loadConversations(): void {
    this.loadingConversations.set(true);
    this.conversationService.getMyConversations().subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          this.conversations.set(res.data.map(dto => this.toChatConversation(dto)));
        }
      },
      complete: () => this.loadingConversations.set(false),
    });
  }

  refreshConversations(): void {
    this.loadConversations();
    this.loadUnreadCount();
  }

  loadUnreadCount(): void {
    this.conversationService.getUnreadCount().subscribe(res => {
      if (res.isSuccess) this.totalUnread.set(res.data);
    });
  }

  selectConversation(id: number): void {
    this.discardPendingVoice();
    const prev = this.activeConversationId();
    if (prev) this.conversationService.leaveConversation(prev);
    this.activeConversationId.set(id);
    this.conversationService.joinConversation(id);
    this.conversationService.markAsReadRest(id).subscribe();
    this.conversationService.markAsReadSignalR(id);
    this.conversations.update(list => list.map(c => c.id === id ? { ...c, unread: false } : c));
    this.loadMessages(id);
    this.showConvList.set(false);
    this.sidebarOpen.set(false);
    this.showUserSearch.set(false);
    this.userSearchQuery.set('');
    this.userSearchResults.set([]);
    this.checkBlockStatus();
  }

  private checkBlockStatus(): void {
    this.isBlocked.set(false);
    this.blockedByOther.set(false);
    const conv = this.activeConversation();
    if (!conv || conv.type === 'group') return;
    const currentUserId = this.authService.user()?.userId;
    const other = conv.participants.find(p => p.userId !== currentUserId);
    if (!other) return;
    this.conversationService.isUserBlocked(conv.id, other.userId).subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          this.isBlocked.set(res.data.isBlocked);
          this.blockedByOther.set(res.data.blockedByOther);
          if (res.data.blockedByMe) {
            this.blockedByMe.update(s => new Set(s).add(other.userId));
          } else {
            this.blockedByMe.update(s => { const n = new Set(s); n.delete(other.userId); return n; });
          }
        }
      },
    });
  }

  backToConversations(): void {
    this.showConvList.set(true);
  }

  toggleProfilePanel(): void {
    this.showProfilePanel.update(v => !v);
  }

  loadMessages(conversationId: number): void {
    this.loadingMessages.set(true);
    this.hasMoreMessages.set(true);
    this.messagesPage.set(1);
    this.conversationService.getMessages(conversationId, 1, this.pageSize).subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          this.hasMoreMessages.set(res.data!.page < res.data!.totalPages);
          const items = (res.data!.items ?? []).reverse();
          this.conversations.update(list => list.map(c => {
            if (c.id === conversationId) {
              return { ...c, messages: items.map(m => this.toChatMessage(m, c.participants)) };
            }
            return c;
          }));
          this.scrollToBottom();
        }
      },
      error: () => this.loadingMessages.set(false),
      complete: () => {
        this.loadingMessages.set(false);
        setTimeout(() => this.enableScrollToLoad(), 100);
      },
    });
  }

  private scrollCleanup: (() => void) | null = null;
  private lastLoadTime = 0;

  private enableScrollToLoad(): void {
    this.scrollCleanup?.();
    const el = this.messagesContainer()?.nativeElement;
    if (!el) {
      setTimeout(() => this.enableScrollToLoad(), 200);
      return;
    }

    const handler = (): void => {
      if (el.scrollTop < 250) {
        const now = Date.now();
        if (now - this.lastLoadTime < 3000) return;
        this.lastLoadTime = now;
        const convId = this.activeConversationId();
        if (convId) this.loadOlderMessages(convId);
      }
    };
    el.addEventListener('scroll', handler, { passive: true });
    this.scrollCleanup = () => el.removeEventListener('scroll', handler);
  }

  private previousScrollHeight = 0;

  loadOlderMessages(conversationId: number): void {
    if (this.loadingOlder() || !this.hasMoreMessages()) return;

    this.loadingOlder.set(true);
    this.cdr.detectChanges();
    const nextPage = this.messagesPage() + 1;
    const el = this.messagesContainer()?.nativeElement;
    this.previousScrollHeight = el?.scrollHeight ?? 0;

    this.conversationService.getMessages(conversationId, nextPage, this.pageSize).subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          this.hasMoreMessages.set(res.data!.page < res.data!.totalPages);
          this.messagesPage.set(nextPage);
          const participants = this.activeConversation()?.participants ?? [];
          const items = (res.data!.items ?? []).reverse().map(m => this.toChatMessage(m, participants));

          this.conversations.update(list => list.map(c => {
            if (c.id === conversationId) {
              return { ...c, messages: [...items, ...c.messages] };
            }
            return c;
          }));

          // Restore scroll position AFTER a delay so user sees the loading indicator
          setTimeout(() => {
            if (el) {
              el.scrollTop = el.scrollHeight - this.previousScrollHeight;
            }
            this.loadingOlder.set(false);
            // Re-attach observer only after scroll position is restored
            setTimeout(() => this.enableScrollToLoad(), 150);
          }, 1000);
        } else {
          this.loadingOlder.set(false);
          setTimeout(() => this.enableScrollToLoad(), 150);
        }
      },
      error: (err) => {
        console.error('Error loading older messages:', err);
        this.loadingOlder.set(false);
        setTimeout(() => this.enableScrollToLoad(), 150);
      },
      complete: () => {},
    });
  }

  sendMessage(): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    const file = this.selectedFile();
    if (file) {
      const text = this.newMessage().trim();
      this.newMessage.set('');
      this.sendWithFile(file, text);
      return;
    }
    const text = this.newMessage().trim();
    if (!text) return;
    this.newMessage.set('');
    this.conversationService.sendMessageRest(convId, text).subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          this.appendMessage(convId, this.toChatMessage(res.data, this.activeConversation()?.participants));
        } else {
          this.showError(res.message || 'حدث خطأ أثناء الإرسال');
          this.newMessage.set(text);
        }
      },
      error: (err: any) => {
        const msg = err?.error?.message || err?.message || 'حدث خطأ في الاتصال';
        this.showError(msg);
        this.newMessage.set(text);
      },
    });
  }

  private appendMessage(convId: number, msg: ChatMessage): void {
    this.conversations.update(list => list.map(c => {
      if (c.id !== convId) return c;
      if (c.messages.some(m => m.id === msg.id)) return c;
      return {
        ...c,
        messages: [...c.messages, { ...msg, senderRole: this.getSenderRole(msg.senderId, c.participants) }],
        lastMessage: msg.content,
        lastTime: this.formatTime(msg.sentAt),
      };
    }));
    
    // Auto-show transcript for sent voice messages
    this.autoShowTranscriptForSent(msg.id, msg.isMine, !!msg.voiceText);

    // Scroll to bottom for new messages (both mine and received)
    this.scrollToBottom();
    
    console.log('Message appended:', {
      id: msg.id,
      isMine: msg.isMine,
      hasVoiceText: !!msg.voiceText,
      voiceText: msg.voiceText,
      content: msg.content
    });
  }

  onTyping(): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    this.conversationService.sendTypingSignalR(convId);
    if (this.typingTimeout) clearTimeout(this.typingTimeout);
    this.typingTimeout = setTimeout(() => {
      this.conversationService.sendStoppedTypingSignalR(convId);
    }, 2000);
  }

  useSuggestion(text: string): void {
    this.newMessage.set(text);
  }

  setActiveFilter(filter: 'all' | 'groups' | 'individual'): void {
    this.activeFilter.set(filter);
  }

  openCreateGroupModal(): void {
    this.groupType.set('all-subjects');
    this.groupTitle.set('');
    this.selectedSubjectId.set(null);
    this.selectedClassId.set(null);
    this.selectedAcademicYearId.set(null);
    this.showCreateGroupModal.set(true);
    this.academicYearService.getCurrent().subscribe(res => {
      const data = res.data ?? res;
      if (data?.id) this.selectedAcademicYearId.set(data.id);
    });
    this.academicYearService.getAll().subscribe(res => {
      const data = res.data ?? res;
      this.academicYearList.set(Array.isArray(data) ? data : []);
    });
    this.classService.getAll({ status: 1 }).subscribe(res => {
      const data = res.data ?? res;
      if (Array.isArray(data)) this.classList.set(data);
      else if (data?.items) this.classList.set(data.items);
    });
    this.subjectService.getAll().subscribe(res => {
      const data = res.data ?? res;
      this.subjectList.set(Array.isArray(data) ? data : []);
    });
  }

  closeCreateGroupModal(): void {
    this.showCreateGroupModal.set(false);
  }

  createGroup(): void {
    const classId = this.selectedClassId();
    const academicYearId = this.selectedAcademicYearId();
    if (!classId || !academicYearId) return;
    const gType = this.groupType();
    if (gType === 'specific-subject' && !this.selectedSubjectId()) return;
    this.creatingGroup.set(true);
    const obs = gType === 'specific-subject'
      ? this.conversationService.createSubjectGroupConversation({
          subjectId: this.selectedSubjectId()!,
          classId,
          academicYearId,
          title: this.groupTitle() || undefined,
        })
      : this.conversationService.createClassGroupConversation({
          classId,
          academicYearId,
          title: this.groupTitle() || undefined,
        });
    obs.subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          this.conversations.update(list => [this.toChatConversation(res.data!), ...list]);
          this.selectConversation(res.data!.id);
          this.closeCreateGroupModal();
        }
      },
      complete: () => this.creatingGroup.set(false),
    });
  }

  isAdminOrTeacher(): boolean {
    const role = this.authService.user()?.role;
    return role === 'admin' || role === 'teacher';
  }

  canBlockUser(targetRole: string): boolean {
    const myRole = this.authService.user()?.role;
    if (!myRole) return false;
    if (myRole === 'admin') return true;
    if (myRole === 'teacher') return targetRole !== 'admin';
    if (myRole === 'student') return targetRole === 'student' || targetRole === 'parent';
    return false;
  }

  openDirectConversation(userId: number): void {
    const currentUserId = this.authService.user()?.userId;
    if (!currentUserId || userId === currentUserId) return;
    const existing = this.conversations().find(c =>
      c.type === 'individual' && c.participants.some(p => p.userId === userId)
    );
    if (existing) {
      this.selectConversation(existing.id);
      return;
    }
    this.conversationService.createDirectConversation({ targetUserId: userId }).subscribe(res => {
      if (res.isSuccess && res.data) {
        this.conversations.update(list => [this.toChatConversation(res.data!), ...list]);
        this.selectConversation(res.data!.id);
      }
    });
  }

  searchUsersForChat(): void {
    if (this.userSearchTimeout) clearTimeout(this.userSearchTimeout);
    const q = this.userSearchQuery().trim();
    if (!q || q.length < 2) {
      this.userSearchResults.set([]);
      return;
    }
    this.userSearchTimeout = setTimeout(() => {
      this.userService.search(q, 20).subscribe({
        next: res => {
          const data = res.data ?? res;
          const items: any[] = Array.isArray(data) ? data : data?.items ?? [];
          const currentUserId = this.authService.user()?.userId;
          const seen = new Set<number>();
          this.userSearchResults.set(items.filter((u: any) => {
            if (u.id === currentUserId) return false;
            if (seen.has(u.id)) return false;
            seen.add(u.id);
            return true;
          }));
        },
        error: () => {},
      });
    }, 300);
  }

  openAddParticipantModal(): void {
    this.showAddParticipantModal.set(true);
    this.showGroupMembersModal.set(false);
    this.searchUserQuery.set('');
    this.searchUserResults.set([]);
  }

  closeAddParticipantModal(): void {
    this.showAddParticipantModal.set(false);
    this.searchUserQuery.set('');
    this.searchUserResults.set([]);
    this.showGroupMembersModal.set(true);
  }

  openGroupMembersModal(): void {
    this.showGroupMembersModal.set(true);
  }

  closeGroupMembersModal(): void {
    this.showGroupMembersModal.set(false);
  }

  searchUsers(): void {
    if (this.searchTimeout) clearTimeout(this.searchTimeout);
    const q = this.searchUserQuery().trim();
    if (!q || q.length < 2) {
      this.searchUserResults.set([]);
      return;
    }
    this.searchTimeout = setTimeout(() => {
      this.userService.search(q, 50).subscribe({
        next: res => {
          const data = res.data ?? res;
          const items: any[] = Array.isArray(data) ? data : data?.items ?? [];
          const conv = this.activeConversation();
          const existingIds = new Set(conv?.participants.map(p => p.userId) ?? []);
          const seen = new Set<number>();
          this.searchUserResults.set(items.filter((u: any) => {
            if (existingIds.has(u.id)) return false;
            if (seen.has(u.id)) return false;
            seen.add(u.id);
            return true;
          }));
        },
        error: () => {},
      });
    }, 300);
  }

  addParticipant(userId: number): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    this.addingParticipant.set(true);
    this.conversationService.addParticipant(convId, userId).subscribe({
      next: res => {
        if (res.isSuccess) {
          this.conversations.update(list => list.map(c => {
            if (c.id === convId) {
              const added = this.searchUserResults().find(u => u.id === userId);
              const newParticipant: ParticipantDto = {
                id: 0,
                userId: added?.id ?? userId,
                userName: added?.fullName ?? 'مستخدم',
                role: added?.role ?? '',
                joinedAt: new Date().toISOString(),
                lastReadAt: null,
              };
              return { ...c, participants: [...c.participants, newParticipant] };
            }
            return c;
          }));
          this.searchUserResults.update(list => list.filter(u => u.id !== userId));
        }
      },
      complete: () => this.addingParticipant.set(false),
    });
  }

  removeParticipant(userId: number): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    this.removingParticipantId.set(userId);
    this.conversationService.removeParticipant(convId, userId).subscribe({
      next: () => {
        this.conversations.update(list => list.map(c => {
          if (c.id === convId) {
            return { ...c, participants: c.participants.filter(p => p.userId !== userId) };
          }
          return c;
        }));
      },
      complete: () => this.removingParticipantId.set(null),
    });
  }

  startEditMessage(msg: ChatMessage): void {
    this.editingMessageId.set(msg.id);
    this.editingMessageContent.set(msg.content);
  }

  cancelEditMessage(): void {
    this.editingMessageId.set(null);
    this.editingMessageContent.set('');
  }

  handleEditKeydown(event: Event): void {
    const kbEvent = event as KeyboardEvent;
    if (kbEvent.shiftKey) return;
    kbEvent.preventDefault();
    this.saveEditMessage();
  }

  saveEditMessage(): void {
    const convId = this.activeConversationId();
    const msgId = this.editingMessageId();
    const content = this.editingMessageContent().trim();
    if (!convId || !msgId || !content) return;
    this.conversationService.updateMessage(convId, msgId, content).subscribe({
      next: () => {
        this.cancelEditMessage();
      },
      error: () => {},
    });
  }

  deleteMessage(convId: number, msgId: number): void {
    if (!confirm('هل أنت متأكد من حذف هذه الرسالة؟')) return;
    this.conversationService.deleteMessage(convId, msgId).subscribe({ error: () => {} });
  }

  selectedFileName = computed(() => this.selectedFile()?.name ?? null);

  handleFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) {
      this.selectedFile.set(input.files[0]);
    }
    input.value = '';
  }

  clearSelectedFile(): void {
    this.selectedFile.set(null);
  }

  private sendWithFile(file: File, text: string): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    this.uploadingFile.set(true);
    this.conversationService.uploadFile(file).subscribe({
      next: res => {
        if (res.isSuccess && res.data) {
          const content = text || res.data.fileName;
          this.conversationService.sendMessageRest(convId, content, res.data.url, file.type || 'application/octet-stream').subscribe({
            next: msgRes => {
              if (msgRes.isSuccess && msgRes.data) {
                this.appendMessage(convId, this.toChatMessage(msgRes.data, this.activeConversation()?.participants));
              } else {
                this.showError(msgRes.message || 'حدث خطأ أثناء الإرسال');
              }
            },
            error: () => this.showError('حدث خطأ أثناء إرسال الملف'),
            complete: () => {
              this.uploadingFile.set(false);
              this.selectedFile.set(null);
            },
          });
        } else {
          this.uploadingFile.set(false);
        }
      },
      error: () => {
        this.showError('حدث خطأ أثناء رفع الملف');
        this.uploadingFile.set(false);
      },
    });
  }

  uploadFile(): void {
    const file = this.selectedFile();
    if (!file) return;
    const text = this.newMessage().trim();
    this.newMessage.set('');
    this.sendWithFile(file, text);
  }

  blockUserInConversation(blockedUserId: number): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    if (!confirm('هل أنت متأكد من حظر هذا المستخدم؟')) return;
    this.conversationService.blockUser(convId, blockedUserId).subscribe({
      next: res => {
        if (res.isSuccess) {
          this.blockedByMe.update(s => { const n = new Set(s).add(blockedUserId); return n; });
          this.isBlocked.set(true);
          this.showError('تم حظر المستخدم بنجاح');
        } else {
          this.showError(res.message || 'حدث خطأ');
        }
      },
      error: (err: any) => this.showError(err?.error?.message || 'حدث خطأ'),
    });
  }

  fileTypeIcon(type?: string): string {
    if (!type) return 'insert_drive_file';
    if (type.startsWith('image/')) return 'image';
    if (type.includes('pdf')) return 'picture_as_pdf';
    if (type.includes('word') || type.includes('document')) return 'description';
    if (type.includes('sheet') || type.includes('excel')) return 'table_chart';
    if (type.includes('video')) return 'videocam';
    if (type.includes('audio')) return 'audiotrack';
    return 'insert_drive_file';
  }

  uploadUrl(path: string): string {
    if (path.startsWith('http')) return path;
    return `${getBackendBaseUrl()}${path}`;
  }

  openImageInNewTab(url: string): void {
    window.open(this.uploadUrl(url), '_blank');
  }

  isImageFile(url: string): boolean {
    return /\.(jpg|jpeg|png|gif|webp|bmp|svg)$/i.test(url);
  }

  getFileName(url: string): string {
    const name = url.split('/').pop() ?? 'ملف';
    return name;
  }

  getFileTitle(msg: ChatMessage): string {
    if (!msg.attachmentUrl) return '';
    const ext = msg.attachmentUrl.split('.').pop()?.toLowerCase() ?? '';
    const typeMap: Record<string, string> = {
      pdf: 'PDF', jpg: 'صورة', jpeg: 'صورة', png: 'صورة', gif: 'صورة',
      webp: 'صورة', doc: 'Word', docx: 'Word', xls: 'Excel', xlsx: 'Excel',
      txt: 'نص', mp4: 'فيديو', mov: 'فيديو', avi: 'فيديو',
    };
    const label = typeMap[ext] || ext.toUpperCase();
    return `ملف.${label}`;
  }

  unblockUserInConversation(blockedUserId: number): void {
    const convId = this.activeConversationId();
    if (!convId) return;
    this.conversationService.unblockUser(convId, blockedUserId).subscribe({
      next: res => {
        if (res.isSuccess) {
          this.blockedByMe.update(s => { const n = new Set(s); n.delete(blockedUserId); return n; });
          if (this.blockedByMe().size === 0) this.isBlocked.set(false);
          this.showError('تم إلغاء الحظر بنجاح');
        } else {
          this.showError(res.message || 'حدث خطأ');
        }
      },
      error: (err: any) => this.showError(err?.error?.message || 'حدث خطأ'),
    });
  }

  private toChatConversation(dto: ConversationDto): ChatConversation {
    const currentUserId = this.authService.user()?.userId;
    const isGroup = dto.type === 'Group';
    let name: string;
    if (isGroup) {
      name = dto.title || 'مجموعة';
    } else {
      const other = dto.participants.find(p => p.userId !== currentUserId);
      name = other?.userName || dto.title || 'محادثة';
    }
    const myParticipant = dto.participants.find(p => p.userId === currentUserId);
    const lastReadAt = myParticipant?.lastReadAt ? new Date(myParticipant.lastReadAt).getTime() : 0;
    const lastMsgAt = dto.lastMessageAt ? new Date(dto.lastMessageAt).getTime() : 0;
    const unread = lastMsgAt > lastReadAt;
    return {
      id: dto.id,
      name,
      type: isGroup ? 'group' : 'individual',
      lastMessage: dto.lastMessage?.content ?? '',
      lastTime: this.formatTime(dto.lastMessageAt),
      unread,
      participants: dto.participants.map(p => ({ ...p, role: p.role ?? '' })),
      messages: [],
    };
  }

  private showError(message: string): void {
    this.errorMessage.set(message);
    setTimeout(() => this.errorMessage.set(null), 5000);
  }

  getOtherParticipantRole(conv: ChatConversation): string {
    const currentUserId = this.authService.user()?.userId;
    const other = conv.participants.find(p => p.userId !== currentUserId);
    if (!other) return '';
    const roleMap: Record<string, string> = { 'Admin': 'مدير', 'Teacher': 'مدرس', 'Parent': 'ولي أمر', 'Student': 'طالب' };
    return roleMap[other.role] || other.role;
  }

  private getSenderRole(senderId: number, participants: ParticipantDto[]): string {
    return participants.find(p => p.userId === senderId)?.role ?? '';
  }

  private toChatMessage(dto: MessageDto, participants?: ParticipantDto[]): ChatMessage {
    const currentUserId = this.authService.user()?.userId;
    const role = participants ? this.getSenderRole(dto.senderId, participants) : '';
    
    const chatMessage: ChatMessage = {
      id: dto.id,
      senderId: dto.senderId,
      senderName: dto.senderName,
      senderRole: role,
      content: dto.content,
      sentAt: dto.sentAt,
      isMine: dto.senderId === currentUserId,
      isEdited: dto.isEdited ?? false,
      attachmentUrl: dto.attachmentUrl ?? undefined,
      attachmentType: dto.attachmentType ?? undefined,
      voiceText: dto.voiceText ?? undefined,
    };
    
    console.log('Converting DTO to ChatMessage:', {
      id: dto.id,
      hasVoiceText: !!dto.voiceText,
      voiceText: dto.voiceText,
      attachmentType: dto.attachmentType
    });
    
    return chatMessage;
  }

  private formatTime(dateStr: string): string {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    // Check for invalid date (e.g. 0001-01-01 or NaN)
    if (isNaN(date.getTime()) || date.getFullYear() < 2000) return '';
    const now = new Date();
    const diff = now.getTime() - date.getTime();
    // If diff is negative (future date), show time only
    if (diff < 0) return date.toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' });
    const days = Math.floor(diff / 86400000);
    if (days === 0) return date.toLocaleTimeString('ar-EG', { hour: '2-digit', minute: '2-digit' });
    if (days === 1) return 'أمس';
    if (days < 7) return `منذ ${days} أيام`;
    return date.toLocaleDateString('ar-EG', { month: 'short', day: 'numeric' });
  }
}
