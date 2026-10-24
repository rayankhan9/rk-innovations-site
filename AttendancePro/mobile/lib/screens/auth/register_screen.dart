import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../services/api.dart';
import '../../state/app_state.dart';
import '../../widgets/common.dart';

class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key});
  @override
  State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _form = GlobalKey<FormState>();
  final _code = TextEditingController(), _name = TextEditingController(), _email = TextEditingController();
  final _empId = TextEditingController(), _dept = TextEditingController(), _pw = TextEditingController();
  Map<String, dynamic>? _org;
  String? _orgError;
  bool _busy = false, _hide = true, _agree = false;

  Future<void> _lookup() async {
    final c = _code.text.trim();
    if (c.length < 4) return;
    try {
      final o = await context.read<AppState>().api.orgLookup(c);
      setState(() {
        _org = o;
        _orgError = null;
      });
    } on ApiException catch (e) {
      setState(() {
        _org = null;
        _orgError = e.message;
      });
    }
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    if (_org == null) await _lookup();
    if (!mounted || _org == null) return;
    setState(() => _busy = true);
    try {
      await context.read<AppState>().register({
        'orgCode': _code.text.trim().toUpperCase(),
        'fullName': _name.text.trim(),
        'email': _email.text.trim(),
        'password': _pw.text,
        'employeeCode': _empId.text.trim().isEmpty ? null : _empId.text.trim(),
        'department': _dept.text.trim().isEmpty ? null : _dept.text.trim(),
      });
      if (mounted) Navigator.of(context).popUntil((r) => r.isFirst);
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message, error: true);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final label = (_org?['memberLabel'] as String?) ?? 'Member';
    return Scaffold(
      appBar: AppBar(title: const Text('Create account')),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Form(
            key: _form,
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Text('Ask your administrator for the organization code.', style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
              const SizedBox(height: 14),
              TextFormField(
                controller: _code,
                textCapitalization: TextCapitalization.characters,
                decoration: InputDecoration(
                    labelText: 'Organization code',
                    prefixIcon: const Icon(Icons.apartment),
                    suffixIcon: IconButton(icon: const Icon(Icons.search), onPressed: _lookup),
                    errorText: _orgError),
                validator: (v) => (v == null || v.trim().length < 4) ? 'Enter the code' : null,
                onChanged: (_) => _org = null,
                onFieldSubmitted: (_) => _lookup(),
              ),
              if (_org != null)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Card(
                    color: Theme.of(context).colorScheme.primaryContainer,
                    child: ListTile(
                      leading: const Icon(Icons.verified, color: Color(0xFF059669)),
                      title: Text(_org!['name']),
                      subtitle: Text(_org!['requiresApproval'] == true ? 'Your account will need administrator approval.' : 'You can start right away.'),
                    ),
                  ),
                ),
              const SizedBox(height: 14),
              TextFormField(controller: _name, textCapitalization: TextCapitalization.words, decoration: const InputDecoration(labelText: 'Full name', prefixIcon: Icon(Icons.person_outline)), validator: (v) => (v == null || v.trim().length < 2) ? 'Enter your name' : null),
              const SizedBox(height: 14),
              TextFormField(controller: _email, keyboardType: TextInputType.emailAddress, decoration: const InputDecoration(labelText: 'Email', prefixIcon: Icon(Icons.email_outlined), helperText: 'Your monthly attendance report is sent here'), validator: (v) => (v == null || !v.contains('@')) ? 'Enter a valid email' : null),
              const SizedBox(height: 14),
              Row(children: [
                Expanded(child: TextFormField(controller: _empId, decoration: InputDecoration(labelText: '$label ID / roll no.', prefixIcon: const Icon(Icons.badge_outlined)))),
                const SizedBox(width: 12),
                Expanded(child: TextFormField(controller: _dept, decoration: const InputDecoration(labelText: 'Department / class'))),
              ]),
              const SizedBox(height: 14),
              TextFormField(
                controller: _pw,
                obscureText: _hide,
                decoration: InputDecoration(
                  labelText: 'Password',
                  prefixIcon: const Icon(Icons.lock_outline),
                  helperText: 'At least 8 characters, including a number',
                  suffixIcon: IconButton(icon: Icon(_hide ? Icons.visibility : Icons.visibility_off), onPressed: () => setState(() => _hide = !_hide)),
                ),
                validator: (v) => (v == null || v.length < 8 || !v.contains(RegExp(r'\d')) || !v.contains(RegExp(r'[a-z]'))) ? 'Use 8+ characters with a number and a lowercase letter' : null,
              ),
              const SizedBox(height: 8),
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                value: _agree,
                onChanged: (v) => setState(() => _agree = v ?? false),
                title: const Text('I understand my location is checked only when I sign in or out, and shared with my organization\'s administrators.', style: TextStyle(fontSize: 13)),
              ),
              const SizedBox(height: 8),
              FilledButton(onPressed: (_busy || !_agree) ? null : _submit, child: _busy ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Register')),
            ]),
          ),
        ),
      ),
    );
  }
}
